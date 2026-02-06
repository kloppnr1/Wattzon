using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<TestDatabase>;
