using System.Collections.Generic;
using Verse;

namespace CerebrexFlavourPack;

public class PrisonerProcessorTracker : MapComponent
{
    private readonly List<CompPrisonerProcessor> processors = new();
    public PrisonerProcessorTracker(Map map) : base(map) { }
    public void Register(CompPrisonerProcessor comp) => processors.Add(comp);
    public void Deregister(CompPrisonerProcessor comp) => processors.Remove(comp);
    public IReadOnlyList<CompPrisonerProcessor> Processors => processors;
}
