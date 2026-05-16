namespace WrathCombo.API.Error;

public class Exception(string? msg, System.Exception? ex)
    : System.Exception(msg, ex);