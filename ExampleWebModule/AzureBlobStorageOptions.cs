using System.ComponentModel.DataAnnotations;

namespace ExampleWebModule;

public class AzureBlobStorageOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = default!;

    [Required(AllowEmptyStrings = false)]
    public string ExamplesContainerName { get; set; } = default!;
}