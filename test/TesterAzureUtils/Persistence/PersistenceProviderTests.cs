using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;
using Xunit.Abstractions;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using Orleans.Providers;
using Orleans.Storage;
using TestExtensions;
using UnitTests.StorageTests;
using UnitTests.Persistence;
using Samples.StorageProviders;

namespace Tester.AzureUtils.Persistence
{
    [Collection(TestEnvironmentFixture.DefaultCollection)]
    [TestCategory("Persistence")]
    public class PersistenceProviderTests_Local
    {
        private readonly IProviderRuntime providerRuntime;
        private readonly Dictionary<string, string> providerCfgProps = new Dictionary<string, string>();
        private readonly ITestOutputHelper output;
        private readonly TestEnvironmentFixture fixture;

        public PersistenceProviderTests_Local(ITestOutputHelper output, TestEnvironmentFixture fixture)
        {
            this.output = output;
            this.fixture = fixture;
            this.providerRuntime = new ClientProviderRuntime(fixture.InternalGrainFactory, fixture.Services, NullLoggerFactory.Instance);
            this.providerCfgProps.Clear();
        }

        [Fact, TestCategory("Functional")]
        public async Task PersistenceProvider_Mock_WriteRead()
        {
            const string testName = nameof(PersistenceProvider_Mock_WriteRead);

            IStorageProvider store = new MockStorageProvider();
            var cfg = new ProviderConfiguration(this.providerCfgProps);
            await store.Init(testName, this.providerRuntime, cfg);

            await Test_PersistenceProvider_WriteRead(testName, store);
        }

        [Fact, TestCategory("Functional")]
        public async Task PersistenceProvider_FileStore_WriteRead()
        {
            const string testName = nameof(PersistenceProvider_FileStore_WriteRead);

            IStorageProvider store = new OrleansFileStorage();
            this.providerCfgProps.Add("RootDirectory", "Data");
            var cfg = new ProviderConfiguration(this.providerCfgProps);
            await store.Init(testName, this.providerRuntime, cfg);

            await Test_PersistenceProvider_WriteRead(testName, store);
        }

        [SkippableFact, TestCategory("Functional"), TestCategory("Azure")]
        public async Task PersistenceProvider_Azure_Read()
        {
            TestUtils.CheckForAzureStorage();
            const string testName = nameof(PersistenceProvider_Azure_Read);

            AzureTableGrainStorage store = await InitAzureTableGrainStorage();
            await Test_PersistenceProvider_Read(testName, store);
        }

        [SkippableTheory, TestCategory("Functional"), TestCategory("Azure")]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(15 * 64 * 1024 - 256, false)]
        [InlineData(15 * 32 * 1024 - 256, true)]
        public async Task PersistenceProvider_Azure_WriteRead(int? stringLength, bool useJson)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4})",
                nameof(PersistenceProvider_Azure_WriteRead),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                nameof(useJson), useJson);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);

            var store = await InitAzureTableGrainStorage(useJson);

            await Test_PersistenceProvider_WriteRead(testName, store, grainState);
        }

        [SkippableTheory, TestCategory("Functional"), TestCategory("Azure")]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(15 * 64 * 1024 - 256, false)]
        [InlineData(15 * 32 * 1024 - 256, true)]
        public async Task PersistenceProvider_Azure_WriteClearRead(int? stringLength, bool useJson)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4})",
                nameof(PersistenceProvider_Azure_WriteClearRead),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                nameof(useJson), useJson);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);

            var store = await InitAzureTableGrainStorage(useJson);

            await Test_PersistenceProvider_WriteClearRead(testName, store, grainState);
        }

        [SkippableTheory, TestCategory("Functional"), TestCategory("Azure")]
        [InlineData(null, true, false)]
        [InlineData(null, false, true)]
        [InlineData(15 * 32 * 1024 - 256, true, false)]
        [InlineData(15 * 32 * 1024 - 256, false, true)]
        public async Task PersistenceProvider_Azure_ChangeReadFormat(int? stringLength, bool useJsonForWrite, bool useJsonForRead)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4}, {5} = {6})",
                nameof(PersistenceProvider_Azure_ChangeReadFormat),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                nameof(useJsonForWrite), useJsonForWrite,
                nameof(useJsonForRead), useJsonForRead);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);
            var grainId = GrainId.NewId();

            var store = await InitAzureTableGrainStorage(useJsonForWrite);

            grainState = await Test_PersistenceProvider_WriteRead(testName, store,
                grainState, grainId);

            store = await InitAzureTableGrainStorage(useJsonForRead);

            await Test_PersistenceProvider_Read(testName, store, grainState, grainId);
        }

        [SkippableTheory, TestCategory("Functional"), TestCategory("Azure")]
        [InlineData(null, true, false)]
        [InlineData(null, false, true)]
        [InlineData(15 * 32 * 1024 - 256, true, false)]
        [InlineData(15 * 32 * 1024 - 256, false, true)]
        public async Task PersistenceProvider_Azure_ChangeWriteFormat(int? stringLength, bool useJsonForFirstWrite, bool useJsonForSecondWrite)
        {
            var testName = string.Format("{0}({1}={2},{3}={4},{5}={6})",
                nameof(PersistenceProvider_Azure_ChangeWriteFormat),
                nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
                "json1stW", useJsonForFirstWrite,
                "json2ndW", useJsonForSecondWrite);

            var grainState = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(grainState);

            var grainId = GrainId.NewId();

            var store = await InitAzureTableGrainStorage(useJsonForFirstWrite);

            await Test_PersistenceProvider_WriteRead(testName, store, grainState, grainId);

            grainState = TestStoreGrainState.NewRandomState(stringLength);
            grainState.ETag = "*";

            store = await InitAzureTableGrainStorage(useJsonForSecondWrite);

            await Test_PersistenceProvider_WriteRead(testName, store, grainState, grainId);
        }

        [SkippableTheory, TestCategory("Functional"), TestCategory("Azure")]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(15 * 64 * 1024 - 256, false)]
        [InlineData(15 * 32 * 1024 - 256, true)]
        public async Task AzureTableStorage_ConvertToFromStorageFormat(int? stringLength, bool useJson)
        {
            var testName = string.Format("{0}({1} = {2}, {3} = {4})",
               nameof(AzureTableStorage_ConvertToFromStorageFormat),
               nameof(stringLength), stringLength == null ? "default" : stringLength.ToString(),
               nameof(useJson), useJson);

            var state = TestStoreGrainState.NewRandomState(stringLength);
            EnsureEnvironmentSupportsState(state);

            var storage = await InitAzureTableGrainStorage(useJson);
            var initialState = state.State;

            var entity = new DynamicTableEntity();

            storage.ConvertToStorageFormat(initialState, entity);

            var convertedState = (TestStoreGrainState)storage.ConvertFromStorageFormat(entity);
            Assert.NotNull(convertedState);
            Assert.Equal(initialState.A, convertedState.A);
            Assert.Equal(initialState.B, convertedState.B);
            Assert.Equal(initialState.C, convertedState.C);
        }

        [Fact, TestCategory("Functional"), TestCategory("MemoryStore")]
        public async Task PersistenceProvider_Memory_FixedLatency_WriteRead()
        {
            const string testName = nameof(PersistenceProvider_Memory_FixedLatency_WriteRead);
            TimeSpan expectedLatency = TimeSpan.FromMilliseconds(200);
            MemoryGrainStorageWithLatency store = new MemoryGrainStorageWithLatency(testName, new MemoryStorageWithLatencyOptions()
            {
                Latency = expectedLatency,
                MockCallsOnly = true
            }, NullLoggerFactory.Instance, this.providerRuntime.ServiceProvider.GetService<IGrainFactory>());

            GrainReference reference = this.fixture.InternalGrainFactory.GetGrain(GrainId.NewId());
            var state = TestStoreGrainState.NewRandomState();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            await store.WriteStateAsync(testName, reference, state);
            TimeSpan writeTime = sw.Elapsed;
            this.output.WriteLine("{0} - Write time = {1}", store.GetType().FullName, writeTime);
            Assert.True(writeTime >= expectedLatency, $"Write: Expected minimum latency = {expectedLatency} Actual = {writeTime}");

            sw.Restart();
            var storedState = new GrainState<TestStoreGrainState>();
            await store.ReadStateAsync(testName, reference, storedState);
            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Read time = {1}", store.GetType().FullName, readTime);
            Assert.True(readTime >= expectedLatency, $"Read: Expected minimum latency = {expectedLatency} Actual = {readTime}");
        }

        [Fact, TestCategory("Performance"), TestCategory("JSON")]
        public void Json_Perf_Newtonsoft_vs_Net()
        {
            const int numIterations = 10000;

            Dictionary<string, object> dataValues = new Dictionary<string, object>();
            var dotnetJsonSerializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string jsonData = null;
            int[] idx = { 0 };
            TimeSpan baseline = TestUtils.TimeRun(numIterations, TimeSpan.Zero, ".Net JavaScriptSerializer",
            () =>
            {
                dataValues.Clear();
                dataValues.Add("A", idx[0]++);
                dataValues.Add("B", idx[0]++);
                dataValues.Add("C", idx[0]++);
                jsonData = dotnetJsonSerializer.Serialize(dataValues);
            });
            idx[0] = 0;
            TimeSpan elapsed = TestUtils.TimeRun(numIterations, baseline, "Newtonsoft Json JavaScriptSerializer",
            () =>
            {
                dataValues.Clear();
                dataValues.Add("A", idx[0]++);
                dataValues.Add("B", idx[0]++);
                dataValues.Add("C", idx[0]++);
                jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(dataValues);
            });
            this.output.WriteLine("Elapsed: {0} Date: {1}", elapsed, jsonData);
        }

        [Fact, TestCategory("Functional")]
        public void LoadClassByName()
        {
            string className = typeof(MockStorageProvider).FullName;
            Type classType = new CachedTypeResolver().ResolveType(className);
            Assert.NotNull(classType); // Type
            Assert.True(typeof(IStorageProvider).IsAssignableFrom(classType), $"Is an IStorageProvider : {classType.FullName}");
        }

        #region Utility functions

        private async Task<AzureTableGrainStorage> InitAzureTableGrainStorage(AzureTableStorageOptions options)
        {
            AzureTableGrainStorage store = ActivatorUtilities.CreateInstance<AzureTableGrainStorage>(this.providerRuntime.ServiceProvider, options, "TestStorage");
            SiloLifecycle lifecycle = ActivatorUtilities.CreateInstance<SiloLifecycle>(this.providerRuntime.ServiceProvider);
            store.Participate(lifecycle);
            await lifecycle.OnStart();
            return store;
        }

        private Task<AzureTableGrainStorage> InitAzureTableGrainStorage(bool useJson = false)
        {
            var options = new AzureTableStorageOptions
            {
                ConnectionString = TestDefaultConfiguration.DataConnectionString,
                UseJson = useJson
            };
            return InitAzureTableGrainStorage(options);
        }

        private async Task Test_PersistenceProvider_Read(string grainTypeName, IGrainStorage store,
            GrainState<TestStoreGrainState> grainState = null, GrainId grainId = null)
        {
            var reference = this.fixture.InternalGrainFactory.GetGrain(grainId ?? GrainId.NewId());

            if (grainState == null)
            {
                grainState = new GrainState<TestStoreGrainState>(new TestStoreGrainState());
            }
            var storedGrainState = new GrainState<TestStoreGrainState>(new TestStoreGrainState());

            Stopwatch sw = new Stopwatch();
            sw.Start();

            await store.ReadStateAsync(grainTypeName, reference, storedGrainState);

            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Read time = {1}", store.GetType().FullName, readTime);

            var storedState = storedGrainState.State;
            Assert.Equal(grainState.State.A, storedState.A);
            Assert.Equal(grainState.State.B, storedState.B);
            Assert.Equal(grainState.State.C, storedState.C);
        }

        private async Task<GrainState<TestStoreGrainState>> Test_PersistenceProvider_WriteRead(string grainTypeName,
            IGrainStorage store, GrainState<TestStoreGrainState> grainState = null, GrainId grainId = null)
        {
            GrainReference reference = this.fixture.InternalGrainFactory.GetGrain(grainId ?? GrainId.NewId());

            if (grainState == null)
            {
                grainState = TestStoreGrainState.NewRandomState();
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            await store.WriteStateAsync(grainTypeName, reference, grainState);

            TimeSpan writeTime = sw.Elapsed;
            sw.Restart();

            var storedGrainState = new GrainState<TestStoreGrainState>
            {
                State = new TestStoreGrainState()
            };
            await store.ReadStateAsync(grainTypeName, reference, storedGrainState);
            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Write time = {1} Read time = {2}", store.GetType().FullName, writeTime, readTime);
            Assert.Equal(grainState.State.A, storedGrainState.State.A);
            Assert.Equal(grainState.State.B, storedGrainState.State.B);
            Assert.Equal(grainState.State.C, storedGrainState.State.C);

            return storedGrainState;
        }

        private async Task<GrainState<TestStoreGrainState>> Test_PersistenceProvider_WriteClearRead(string grainTypeName,
            IGrainStorage store, GrainState<TestStoreGrainState> grainState = null, GrainId grainId = null)
        {
            GrainReference reference = this.fixture.InternalGrainFactory.GetGrain(grainId ?? GrainId.NewId());

            if (grainState == null)
            {
                grainState = TestStoreGrainState.NewRandomState();
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            await store.WriteStateAsync(grainTypeName, reference, grainState);

            TimeSpan writeTime = sw.Elapsed;
            sw.Restart();

            await store.ClearStateAsync(grainTypeName, reference, grainState);

            var storedGrainState = new GrainState<TestStoreGrainState>
            {
                State = new TestStoreGrainState()
            };
            await store.ReadStateAsync(grainTypeName, reference, storedGrainState);
            TimeSpan readTime = sw.Elapsed;
            this.output.WriteLine("{0} - Write time = {1} Read time = {2}", store.GetType().FullName, writeTime, readTime);
            Assert.NotNull(storedGrainState.State);
            Assert.Equal(default(string), storedGrainState.State.A);
            Assert.Equal(default(int), storedGrainState.State.B);
            Assert.Equal(default(long), storedGrainState.State.C);

            return storedGrainState;
        }

        private static void EnsureEnvironmentSupportsState(GrainState<TestStoreGrainState> grainState)
        {
            if (grainState.State.A.Length > 400 * 1024)
            {
                StorageEmulatorUtilities.EnsureEmulatorIsNotUsed();
            }

            TestUtils.CheckForAzureStorage();
        }

        #endregion Utility functions
    }
}