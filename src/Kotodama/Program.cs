using Microsoft.Extensions.Logging;

var logDirectory = Kotodama.ApplicationPaths.GetLogDirectory(AppContext.BaseDirectory);
using var loggerProvider = new Kotodama.DailyFileLoggerProvider(logDirectory, TimeProvider.System);
using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
using var exceptionHandler = new Kotodama.GlobalExceptionHandler(loggerFactory.CreateLogger("Kotodama.Process"));
exceptionHandler.Register();
return await exceptionHandler.RunAsync(() => Kotodama.KotodamaApplication.RunAsync(args));
