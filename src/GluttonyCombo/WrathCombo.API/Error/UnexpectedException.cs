namespace WrathCombo.API.Error;

public class UnexpectedException(string? msg, System.Exception? ex)
    : Exception(msg, ex);