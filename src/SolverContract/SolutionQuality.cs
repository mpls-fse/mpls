namespace Mpls.SolverContract
{
    using System.Runtime.Serialization;

    [DataContract]
    public enum SolutionQuality
    {
        FoundExact,

        Partial,

        Unfeasible,

        None,
    }
}