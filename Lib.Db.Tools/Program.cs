using Lib.Db.Tools;

return await LibDbToolsApplication.RunAsync(
    args,
    new SystemToolConsole(),
    CancellationToken.None);
