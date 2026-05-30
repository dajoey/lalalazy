namespace LazyFateAutomation.Helpers.Enums;

public class SheetAcronyms {
    public record StructAcronym(string Content, string Name, string Meaning);

    public StructAcronym WKS = new("Moon", "Wakusei", "Planet");
    public StructAcronym MJI = new("Island Sanctuary", "Mujinto", "Uninhabited Island");
    public StructAcronym HWD = new("Diadem", "Heavensward Development", "");
    public StructAcronym MKD = new("Occult Crescent", "Mikadoshima", "Crescent Island");
    public StructAcronym DTR = new("Server Info Bar", "DateTime Realm", "");

    public StructAcronym AOZ = new("", "", "");
    public StructAcronym MYC = new("", "", "");
    public StructAcronym EMJ = new("", "", "");
    public StructAcronym VVD = new("", "", "");
    public StructAcronym IKD = new("", "", "");
    public StructAcronym UDS = new("", "", "");
}
