namespace Mpls
{
    using Mpls.Env;
    using Mpls.Solver;
    class Program
    {
        static void Main(string[] args)
        {
            AllocationCase.SimpleAllocationCase<MplsAllocationSolver>();
        }
    }
}
