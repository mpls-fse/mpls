using System;
using System.Diagnostics;
using System.Linq;
using Mpls.Model;
using Mpls.SolverContract;

namespace Mpls.Env
{
    public class AllocationCase
    {
        public static void SimpleAllocationCase<T>() where T : IAllocationSolver, new()
        {
            Console.WriteLine($"Allocating with Solver: {typeof(T).Name}");

            MachineBuilder machineBuilder = new MachineBuilder();

            Machine[] machines =
                machineBuilder.WithCpu(20).WithMemory(20).BuildMany(93).Union(
                    machineBuilder.WithCpu(5).WithMemory(5).BuildMany(51)
                ).Union(
                    machineBuilder.WithCpu(1).WithMemory(1).BuildMany(84)
                ).ToArray();

            ContainerBuilder containerBuilder = new ContainerBuilder();
            ContainerSpec[] containers =
                containerBuilder.WithCpu(1).WithMemory(1).WithInstanceCount(24).BuildMany(1);

            SolverParameters parameters = new SolverParameters
            { Machines = machines, ContainerSpecs = containers, FullScanIterations = 2, InitialRandomGuessIterations = 2 };

            IAllocationSolver solver = new T();
            SolverResult solution = solver.Solve(parameters);

            Console.WriteLine($"Solution Quality: {solution.SolutionQuality.ToString()}");

            var allocatedMachines = solution.NewAllocations.Select(c => new { c.Machine.Name }).ToArray();
            foreach (var allocation in solution.Allocations)
            {
                Console.WriteLine($"{allocation.Machine.Name} <- {allocation.ContainerSpec.Name}");
            }
        }
    }
}

