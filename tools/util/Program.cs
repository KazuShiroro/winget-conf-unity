using ConsoleAppFramework;
using Util.Commands;

var app = ConsoleApp.Create();
app.Add("build-windows", BuildWindowsCommand.Run);
app.Add("check-unity-meta", CheckUnityMetaCommand.Run);
await app.RunAsync(args);
