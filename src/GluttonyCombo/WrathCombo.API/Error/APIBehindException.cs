namespace WrathCombo.API.Error;

public class APIBehindException(string? msg, System.Exception? ex)
    : Exception(msg, ex);