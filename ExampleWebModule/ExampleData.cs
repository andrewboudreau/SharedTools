namespace ExampleWebModule;

public class ExampleData
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
}
