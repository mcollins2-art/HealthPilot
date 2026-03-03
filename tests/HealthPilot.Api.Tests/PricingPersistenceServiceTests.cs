using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace HealthPilot.Api.Tests;

public class PricingPersistenceServiceTests
{
	[Fact]
	public async Task UpsertPricingDataAsync_WithEmptyInput_ReturnsZeroCounts()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var result = await service.UpsertPricingDataAsync([], CancellationToken.None);

		Assert.Equal(0, result.RecordsReceived);
		Assert.Equal(0, result.RecordsSkipped);
		Assert.Equal(0, result.NegotiatedRatesUpserted);
		Assert.Equal(0, result.CashPricesUpserted);
	}

	[Theory]
	[InlineData("", "Brain MRI", "imaging", "General Hospital", "New York", "NY", "10001")]
	[InlineData("70553", "", "imaging", "General Hospital", "New York", "NY", "10001")]
	[InlineData("70553", "Brain MRI", "", "General Hospital", "New York", "NY", "10001")]
	[InlineData("70553", "Brain MRI", "imaging", "", "New York", "NY", "10001")]
	[InlineData("70553", "Brain MRI", "imaging", "General Hospital", "", "NY", "10001")]
	[InlineData("70553", "Brain MRI", "imaging", "General Hospital", "New York", "", "10001")]
	[InlineData("70553", "Brain MRI", "imaging", "General Hospital", "New York", "NY", "")]
	[InlineData("   ", "Brain MRI", "imaging", "General Hospital", "New York", "NY", "10001")]
	public async Task UpsertPricingDataAsync_SkipsStructurallyInvalidRecords(
		string cpt,
		string description,
		string category,
		string facility,
		string city,
		string state,
		string zip)
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var record = CreateRecord(cptCode: cpt, procedureDescription: description, procedureCategory: category, facilityName: facility, city: city, state: state, zip: zip);
		var result = await service.UpsertPricingDataAsync([record], CancellationToken.None);

		Assert.Equal(1, result.RecordsReceived);
		Assert.Equal(1, result.RecordsSkipped);
		Assert.Equal(0, result.ProceduresCreated);
		Assert.Equal(0, result.FacilitiesCreated);
		Assert.Equal(0, result.InsurersCreated);
		Assert.Equal(0, result.NegotiatedRatesUpserted);
		Assert.Equal(0, result.CashPricesUpserted);
	}

	[Fact]
	public async Task UpsertPricingDataAsync_CreatesReferenceDataAndRates_ForValidRecord()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var result = await service.UpsertPricingDataAsync([CreateRecord()], CancellationToken.None);

		Assert.Equal(1, result.ProceduresCreated);
		Assert.Equal(1, result.FacilitiesCreated);
		Assert.Equal(1, result.InsurersCreated);
		Assert.Equal(1, result.NegotiatedRatesUpserted);
		Assert.Equal(1, result.CashPricesUpserted);
		Assert.Equal(1, await fixture.DbContext.Procedures.CountAsync());
		Assert.Equal(1, await fixture.DbContext.Facilities.CountAsync());
		Assert.Equal(1, await fixture.DbContext.Insurers.CountAsync());
		Assert.Equal(1, await fixture.DbContext.NegotiatedRates.CountAsync());
		Assert.Equal(1, await fixture.DbContext.CashPrices.CountAsync());
	}

	[Theory]
	[InlineData(1200, 1300, "contracted", "contracted")]
	[InlineData(1200, 1400, "contracted", "bundled")]
	[InlineData(1000, 999, "discounted", "discounted")]
	[InlineData(500, 650, "contracted", "case_rate")]
	[InlineData(2200, 2100, "contracted", "contracted")]
	[InlineData(750, 850, "facility", "professional")]
	public async Task UpsertPricingDataAsync_UpdatesExistingNegotiatedRate(
		decimal initialRate,
		decimal updatedRate,
		string initialType,
		string updatedType)
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		await service.UpsertPricingDataAsync([CreateRecord(negotiatedRate: initialRate, negotiatedRateType: initialType)], CancellationToken.None);
		await service.UpsertPricingDataAsync([CreateRecord(negotiatedRate: updatedRate, negotiatedRateType: updatedType)], CancellationToken.None);

		var stored = await fixture.DbContext.NegotiatedRates.SingleAsync();
		Assert.Equal(updatedRate, stored.Rate);
		Assert.Equal(updatedType, stored.RateType);
	}

	[Theory]
	[InlineData(800, 790)]
	[InlineData(900, 905)]
	[InlineData(1000, 1200)]
	[InlineData(1500, 1400)]
	[InlineData(400, 450)]
	[InlineData(2500, 2600)]
	public async Task UpsertPricingDataAsync_UpdatesExistingCashPrice(decimal initialPrice, decimal updatedPrice)
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		await service.UpsertPricingDataAsync([CreateRecord(cashPrice: initialPrice)], CancellationToken.None);
		await service.UpsertPricingDataAsync([CreateRecord(cashPrice: updatedPrice)], CancellationToken.None);

		var stored = await fixture.DbContext.CashPrices.SingleAsync();
		Assert.Equal(updatedPrice, stored.CashPriceAmount);
	}

	[Theory]
	[InlineData("Aetna", "Aetna", 1)]
	[InlineData("Aetna", "aetna", 1)]
	[InlineData("BlueCross", "BlueCross", 1)]
	[InlineData("BlueCross", "BlueCross PPO", 2)]
	[InlineData("United", "United", 1)]
	[InlineData("United", "United Commercial", 2)]
	public async Task UpsertPricingDataAsync_HandlesInsurerDeduplication(
		string insurerOne,
		string insurerTwo,
		int expectedInsurers)
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var first = CreateRecord(insurerName: insurerOne);
		var second = CreateRecord(insurerName: insurerTwo, facilityName: "Regional Medical Center", zip: "10002");

		await service.UpsertPricingDataAsync([first, second], CancellationToken.None);

		Assert.Equal(expectedInsurers, await fixture.DbContext.Insurers.CountAsync());
	}

	[Theory]
	[InlineData(" General Hospital ", "New York", "ny", "10001", 1)]
	[InlineData("General Hospital", "New York", "NY", "10001", 1)]
	[InlineData("General Hospital", "New York", "NY", "10002", 2)]
	[InlineData("General Hospital", "Albany", "NY", "12207", 2)]
	[InlineData("City Imaging", "New York", "NY", "10001", 2)]
	[InlineData("General Hospital", "new york", "ny", "10001", 1)]
	public async Task UpsertPricingDataAsync_HandlesFacilityKeying(
		string secondName,
		string secondCity,
		string secondState,
		string secondZip,
		int expectedFacilityCount)
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var first = CreateRecord();
		var second = CreateRecord(facilityName: secondName, city: secondCity, state: secondState, zip: secondZip, insurerName: "BlueCross");

		await service.UpsertPricingDataAsync([first, second], CancellationToken.None);

		Assert.Equal(expectedFacilityCount, await fixture.DbContext.Facilities.CountAsync());
	}

	[Theory]
	[InlineData("70553", "70553", 1)]
	[InlineData("70553", "70553 ", 1)]
	[InlineData("70553", " 70553", 1)]
	[InlineData("70553", "70554", 2)]
	[InlineData("70450", "70450", 1)]
	[InlineData("70450", "70553", 2)]
	public async Task UpsertPricingDataAsync_HandlesProcedureDeduplication(
		string firstCpt,
		string secondCpt,
		int expectedProcedureCount)
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var first = CreateRecord(cptCode: firstCpt);
		var second = CreateRecord(cptCode: secondCpt, facilityName: "Regional Medical Center", zip: "10002", insurerName: "BlueCross");

		await service.UpsertPricingDataAsync([first, second], CancellationToken.None);

		Assert.Equal(expectedProcedureCount, await fixture.DbContext.Procedures.CountAsync());
	}

	[Fact]
	public async Task UpsertPricingDataAsync_WithoutInsurerOnlyPersistsCashPrice()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var result = await service.UpsertPricingDataAsync([
			CreateRecord(insurerName: null, negotiatedRate: 1200m, cashPrice: 980m)
		], CancellationToken.None);

		Assert.Equal(0, result.InsurersCreated);
		Assert.Equal(0, result.NegotiatedRatesUpserted);
		Assert.Equal(1, result.CashPricesUpserted);
		Assert.Equal(0, await fixture.DbContext.NegotiatedRates.CountAsync());
		Assert.Equal(1, await fixture.DbContext.CashPrices.CountAsync());
	}

	[Fact]
	public async Task UpsertPricingDataAsync_WithoutCashPrice_ProducesZeroCashUpserts()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var result = await service.UpsertPricingDataAsync([
			CreateRecord(cashPrice: null, negotiatedRate: 1200m, insurerName: "Aetna")
		], CancellationToken.None);

		Assert.Equal(0, result.CashPricesUpserted);
		Assert.Equal(1, result.NegotiatedRatesUpserted);
		Assert.Equal(0, await fixture.DbContext.CashPrices.CountAsync());
		Assert.Equal(1, await fixture.DbContext.NegotiatedRates.CountAsync());
	}

	[Fact]
	public async Task UpsertPricingDataAsync_WithBlankFacilityType_StoresUnknownType()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		await service.UpsertPricingDataAsync([
			CreateRecord(facilityType: "   ")
		], CancellationToken.None);

		var facility = await fixture.DbContext.Facilities.SingleAsync();
		Assert.Equal("Unknown", facility.Type);
	}

	[Fact]
	public async Task UpsertPricingDataAsync_WithNullNegotiatedRate_SkipsNegotiatedUpsert()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var result = await service.UpsertPricingDataAsync([
			CreateRecord(negotiatedRate: null, insurerName: "Aetna", cashPrice: 900m)
		], CancellationToken.None);

		Assert.Equal(0, result.NegotiatedRatesUpserted);
		Assert.Equal(1, result.CashPricesUpserted);
		Assert.Equal(0, await fixture.DbContext.NegotiatedRates.CountAsync());
	}

	[Fact]
	public async Task UpsertPricingDataAsync_WithBlankNegotiatedRateType_DefaultsToContracted()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		await service.UpsertPricingDataAsync([
			CreateRecord(negotiatedRateType: "   ")
		], CancellationToken.None);

		var rate = await fixture.DbContext.NegotiatedRates.SingleAsync();
		Assert.Equal("contracted", rate.RateType);
	}

	[Fact]
	public async Task UpsertPricingDataAsync_RollsBackAndThrows_WhenSaveFails()
	{
		await using var fixture = await ThrowingTestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.UpsertPricingDataAsync([CreateRecord()], CancellationToken.None));

		Assert.Equal(0, await fixture.DbContext.Procedures.CountAsync());
		Assert.Equal(0, await fixture.DbContext.Facilities.CountAsync());
		Assert.Equal(0, await fixture.DbContext.Insurers.CountAsync());
	}

	[Fact]
	public async Task UpsertPricingDataAsync_DuplicateRows_DoNotCreateDuplicateCompositeKeys()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var record = CreateRecord(negotiatedRate: 900m, cashPrice: 850m);
		await service.UpsertPricingDataAsync([record, record, record], CancellationToken.None);

		Assert.Equal(1, await fixture.DbContext.NegotiatedRates.CountAsync());
		Assert.Equal(1, await fixture.DbContext.CashPrices.CountAsync());
	}

	[Fact]
	public async Task UpsertPricingDataAsync_MultipleFacilitiesAndInsurers_PersistsAllCombos()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var records = new[]
		{
			CreateRecord(insurerName: "Aetna", facilityName: "General Hospital", zip: "10001", negotiatedRate: 1000m),
			CreateRecord(insurerName: "BlueCross", facilityName: "General Hospital", zip: "10001", negotiatedRate: 1100m),
			CreateRecord(insurerName: "Aetna", facilityName: "Regional Medical Center", zip: "10002", negotiatedRate: 1200m),
			CreateRecord(insurerName: "BlueCross", facilityName: "Regional Medical Center", zip: "10002", negotiatedRate: 1300m)
		};

		await service.UpsertPricingDataAsync(records, CancellationToken.None);

		Assert.Equal(4, await fixture.DbContext.NegotiatedRates.CountAsync());
		Assert.Equal(2, await fixture.DbContext.Insurers.CountAsync());
		Assert.Equal(2, await fixture.DbContext.Facilities.CountAsync());
	}

	[Fact]
	public async Task UpsertNegotiatedRatesAsync_SkipsRows_WhenProcedureOrFacilityLookupMissing()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var records = new[]
		{
			CreateRecord(cptCode: "70553", facilityName: "General Hospital", city: "New York", state: "NY", zip: "10001", insurerName: "Aetna", negotiatedRate: 1200m),
			CreateRecord(cptCode: "70450", facilityName: "Regional Medical Center", city: "New York", state: "NY", zip: "10002", insurerName: "Aetna", negotiatedRate: 1100m)
		};

		var procedureMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["70450"] = 1
		};
		var facilityMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var insurerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["Aetna"] = 1
		};

		var upserted = await InvokePrivateNegotiatedUpsertAsync(service, records, procedureMap, facilityMap, insurerMap);

		Assert.Equal(0, upserted);
	}

	[Fact]
	public async Task UpsertCashPricesAsync_SkipsRows_WhenProcedureOrFacilityLookupMissing()
	{
		await using var fixture = await TestDbFixture.CreateAsync();
		var service = CreateService(fixture.DbContext);

		var records = new[]
		{
			CreateRecord(cptCode: "70553", facilityName: "General Hospital", city: "New York", state: "NY", zip: "10001", cashPrice: 900m),
			CreateRecord(cptCode: "70450", facilityName: "Regional Medical Center", city: "New York", state: "NY", zip: "10002", cashPrice: 850m)
		};

		var procedureMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["70450"] = 1
		};
		var facilityMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		var upserted = await InvokePrivateCashUpsertAsync(service, records, procedureMap, facilityMap);

		Assert.Equal(0, upserted);
	}

	private static PricingPersistenceService CreateService(AppDbContext dbContext)
		=> new(dbContext, NullLogger<PricingPersistenceService>.Instance);

	private static async Task<int> InvokePrivateNegotiatedUpsertAsync(
		PricingPersistenceService service,
		IReadOnlyList<StructuredPricingRecord> records,
		Dictionary<string, int> procedureMap,
		Dictionary<string, int> facilityMap,
		Dictionary<string, int> insurerMap)
	{
		var method = typeof(PricingPersistenceService).GetMethod(
			"UpsertNegotiatedRatesAsync",
			BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.NotNull(method);
		var task = method!.Invoke(service, [records, procedureMap, facilityMap, insurerMap, CancellationToken.None]) as Task<int>;
		Assert.NotNull(task);

		return await task!;
	}

	private static async Task<int> InvokePrivateCashUpsertAsync(
		PricingPersistenceService service,
		IReadOnlyList<StructuredPricingRecord> records,
		Dictionary<string, int> procedureMap,
		Dictionary<string, int> facilityMap)
	{
		var method = typeof(PricingPersistenceService).GetMethod(
			"UpsertCashPricesAsync",
			BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.NotNull(method);
		var task = method!.Invoke(service, [records, procedureMap, facilityMap, CancellationToken.None]) as Task<int>;
		Assert.NotNull(task);

		return await task!;
	}

	private static StructuredPricingRecord CreateRecord(
		string cptCode = "70553",
		string procedureDescription = "Brain MRI",
		string procedureCategory = "imaging",
		string facilityName = "General Hospital",
		string facilityType = "hospital",
		string city = "New York",
		string state = "NY",
		string zip = "10001",
		string? insurerName = "Aetna",
		decimal? negotiatedRate = 1200m,
		string negotiatedRateType = "contracted",
		decimal? cashPrice = 1000m)
	{
		return new StructuredPricingRecord
		{
			CptCode = cptCode,
			ProcedureDescription = procedureDescription,
			ProcedureCategory = procedureCategory,
			FacilityName = facilityName,
			FacilityType = facilityType,
			City = city,
			State = state,
			ZipCode = zip,
			InsurerName = insurerName,
			NegotiatedRate = negotiatedRate,
			NegotiatedRateType = negotiatedRateType,
			CashPrice = cashPrice,
			LastUpdated = DateTimeOffset.UtcNow
		};
	}

	private sealed class TestDbFixture : IAsyncDisposable
	{
		private readonly SqliteConnection _connection;
		public AppDbContext DbContext { get; }

		private TestDbFixture(SqliteConnection connection, AppDbContext dbContext)
		{
			_connection = connection;
			DbContext = dbContext;
		}

		public static async Task<TestDbFixture> CreateAsync()
		{
			var connection = new SqliteConnection("DataSource=:memory:");
			await connection.OpenAsync();

			var options = new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite(connection)
				.Options;

			var dbContext = new AppDbContext(options);
			await dbContext.Database.EnsureCreatedAsync();

			return new TestDbFixture(connection, dbContext);
		}

		public async ValueTask DisposeAsync()
		{
			await DbContext.DisposeAsync();
			await _connection.DisposeAsync();
		}
	}

	private sealed class ThrowingTestDbFixture : IAsyncDisposable
	{
		private readonly SqliteConnection _connection;
		public ThrowingAppDbContext DbContext { get; }

		private ThrowingTestDbFixture(SqliteConnection connection, ThrowingAppDbContext dbContext)
		{
			_connection = connection;
			DbContext = dbContext;
		}

		public static async Task<ThrowingTestDbFixture> CreateAsync()
		{
			var connection = new SqliteConnection("DataSource=:memory:");
			await connection.OpenAsync();

			var options = new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite(connection)
				.Options;

			var dbContext = new ThrowingAppDbContext(options);
			await dbContext.Database.EnsureCreatedAsync();

			return new ThrowingTestDbFixture(connection, dbContext);
		}

		public async ValueTask DisposeAsync()
		{
			await DbContext.DisposeAsync();
			await _connection.DisposeAsync();
		}
	}

	private sealed class ThrowingAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
	{
		public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Forced save failure for coverage.");
		}
	}
}
