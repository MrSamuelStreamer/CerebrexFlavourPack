using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

[DefOf]
public static class CerebrexFlavourPackDefOf
{
    // Remember to annotate any Defs that require a DLC as needed e.g.
    // [MayRequireBiotech]
    // public static GeneDef YourPrefix_YourGeneDefName;
    
    static CerebrexFlavourPackDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CerebrexFlavourPackDefOf));
}
