namespace AutoKupo;

public enum KupoState
{
    Idle,
    ScanningForLizbeth,
    TargetingLizbeth,
    TryingToInteract,
    InDialog,
    SelectingMenu,
    ProcessingCard,
    Done,
    Error,
}
