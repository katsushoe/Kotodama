using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
using var exceptionHandler = new Kotodama.GlobalExceptionHandler(loggerFactory.CreateLogger("Kotodama.Process"));
exceptionHandler.Register();
return await exceptionHandler.RunAsync(() => Kotodama.KotodamaApplication.RunAsync(args));
