namespace Mpls.SolverContract
{
    public interface IAllocationSolver
    {
        SolverResult Solve(SolverParameters parameters);
    }
}