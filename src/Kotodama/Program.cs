try
{
    return await Kotodama.KotodamaApplication.RunAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Kotodama could not start: {exception.Message}");
    return 1;
}
