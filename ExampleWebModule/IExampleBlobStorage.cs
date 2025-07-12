namespace ExampleWebModule;

public interface IExampleBlobStorage
{
    Task SaveAsync(ExampleData exampleData, CancellationToken ct = default);
    Task<ExampleData?> LoadAsync(Guid exampleId, CancellationToken ct = default);
}
