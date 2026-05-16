namespace WrathCombo.API.Error;

public class UninitializedException(string? msg, System.Exception? ex = null)
    : Exception(msg, ex);