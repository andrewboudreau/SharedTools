
using SharedTools.Web;

var app = await WebApplicationExtensions.CreateAsync(args, "ExampleWebModule", "SharedTools.ModuleManagement");
app.Run();
