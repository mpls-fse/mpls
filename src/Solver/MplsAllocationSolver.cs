using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mpls.Model;
using Mpls.SolverContract;
using Mpls.Utils;

namespace Mpls.Solver
{
    public sealed class MplsAllocationSolver : IAllocationSolver
    {
        public SolverResult Solve(SolverParameters parameters)
        {
            MplsAllocationSolverImpl solver = new MplsAllocationSolverImpl(parameters);
            SolverResult solution = solver.Solve();
            return solution;
        }
    }
    internal class MplsAllocationSolverImpl
    {
        private readonly SolverParameters parameters;

        private readonly int[] desiredContainerInstanceCounts;

        private readonly int[][] machineConstraintsPerResource;

        private readonly int[][] containerCoefficientsPerResource;

        private int[] maxDeallocationCount;

        private int[] currentDeallocationCount;

        private byte[][] variables;

        private byte[][] initialState;

        private Stopwatch stopwatch;

        private int? hardConstraintResourceIndex;

        private double[] averageAvailableResource;

        private const double PseudoThreshold = 1.0;

        private double[] resourceWeight;

        public MplsAllocationSolverImpl(SolverParameters parameters)
        {
            this.parameters = parameters;

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (this.parameters.Machines == null || this.parameters.Machines.Length == 0)
            {
                throw new ArgumentException("No Machines where provided as parameters");
            }

            if (this.parameters.ContainerSpecs == null || this.parameters.ContainerSpecs.Length == 0)
            {
                throw new ArgumentException("No Container Specs where provided as parameters");
            }

            this.averageAvailableResource = new double[this.ResourceCount];

            this.resourceWeight = new double[this.ResourceCount];
            for (int i = 0; i < this.ResourceCount; i++)
            {
                this.resourceWeight[i] = 1;
            }

            this.desiredContainerInstanceCounts = parameters.ContainerSpecs
                .Select(c => c.DesiredInstanceCount)
                .ToArray();

            if (parameters.HardConstraintResource.HasValue)
            {
                this.IdentifyHardConstraint(parameters.HardConstraintResource.Value, parameters.OptimizeResources);
            }

            if (parameters.MaximizeResourceUsageFor.HasValue)
            {
                this.AssignInstancesToMaximizeResourceUtilization();
            }

            bool allMachineResourcesListed = parameters.Machines.All(c => c.AvailableResources.Length == parameters.Machines[0].AvailableResources.Length);
            if (!allMachineResourcesListed)
            {
                throw new ArgumentException("All Machines should list the exact same resources, and specify zero when not required.");
            }

            bool allContainerResourceUsagesListed = parameters.ContainerSpecs.All(c => c.ResourceUsages.Length == parameters.ContainerSpecs[0].ResourceUsages.Length);
            if (!allContainerResourceUsagesListed)
            {
                throw new ArgumentException("All Container Specs should list the exact same resources, and specify zero when not required.");
            }

            this.machineConstraintsPerResource = parameters.Machines.Select(c => c.AvailableResources.Select(x => x.Value).ToArray()).ToArray().Rotate();
            this.containerCoefficientsPerResource = parameters.ContainerSpecs.Select(c => c.ResourceUsages.Select(d => d.Value).ToArray()).ToArray().Rotate();

            this.currentDeallocationCount = new int[this.Columns];
        }

        private int Rows => this.parameters.Machines.Length;

        private int Columns => this.parameters.ContainerSpecs.Length;

        private int ResourceCount => this.parameters.OptimizeResources.Length;

        public SolverResult Solve()
        {
            double baselineScore = 0, initialStateScore = 0, score = 0;
            int iterativeSwappingIterations = 0, fullScanIterations = 0, interContainerIterations = 0;
            SolutionQuality result;
            SolverErrorCode errorCode = SolverErrorCode.None;

            this.stopwatch = Stopwatch.StartNew();

            Dictionary<ResourceKind, AllocationMetric[]> initialGuessMetrics = null, solutionMetrics = null;

            this.initialState = this.InitializeMatrix();
            this.variables = this.InitializeMatrix();

            if (this.EnsureSolutionIsViable(ref errorCode))
            {
                this.ComputeAverageAvailableResource();

                if (this.parameters.CurrentAllocations == null)
                {
                    if (this.parameters.InitialRandomGuessIterations > 0)
                    {
                        this.variables = this.GenerateSamples(iterations: this.parameters.InitialRandomGuessIterations);
                    }
                    else
                    {
                        this.variables = this.GenerateInitialAllocation();
                    }
                }
                else
                {
                    this.initialState = this.PopulateInitialState();
                    initialStateScore = ComputeVariance(this.Rows, this.Columns, this.ResourceCount, this.variables, this.containerCoefficientsPerResource, this.machineConstraintsPerResource, this.averageAvailableResource, this.resourceWeight);
                    this.variables = this.GreedyPlacementOverInitialState(this.initialState);
                }

                baselineScore = ComputeVariance(this.Rows, this.Columns, this.ResourceCount, this.variables, this.containerCoefficientsPerResource, this.machineConstraintsPerResource, this.averageAvailableResource, this.resourceWeight);

                initialGuessMetrics = this.CaptureMetrics();

                if (this.parameters.AllowedChurnPercentage.HasValue)
                {
                    int[] currentAllocationsCounts = null;

                    if (this.parameters.CurrentAllocations?.Length > 0)
                    {
                        currentAllocationsCounts = this.GetInitialAllocationCounts();
                    }

                    this.maxDeallocationCount = CalculateMaxDeallocationCounts(
                        this.parameters.AllowedChurnPercentage.Value,
                        this.desiredContainerInstanceCounts,
                        currentAllocationsCounts);
                }

                iterativeSwappingIterations = this.IterativeSwapBestWorstMachinesForContainers(this.parameters.BestWorstSwappingIterations);

                fullScanIterations = this.PerformFullScanSwapping(this.parameters.FullScanIterations);

                interContainerIterations = this.FullScanSwappingContainers(this.parameters.FullScanSwapContainerIterations);

                score = ComputeVariance(this.Rows, this.Columns, this.ResourceCount, this.variables, this.containerCoefficientsPerResource, this.machineConstraintsPerResource, this.averageAvailableResource, this.resourceWeight);

                solutionMetrics = this.CaptureMetrics();

                bool satisfiesContainerInstanceCount = this.AllContainerInstanceCountSatisfied();
                bool satisfiesConstraints = this.AllConstraintsAreSatisfied();

                if (satisfiesContainerInstanceCount && satisfiesConstraints)
                {
                    result = SolutionQuality.FoundExact;
                }
                else if (double.IsNegativeInfinity(score) || double.IsNaN(score))
                {
                    errorCode = SolverErrorCode.AdjustingErrorNegativeInfinityOrNaN;
                    result = SolutionQuality.Unfeasible;
                }
                else if (this.hardConstraintResourceIndex.HasValue && !this.AreHardConstraintsSatisfied())
                {
                    errorCode = SolverErrorCode.HardConstraintUnsatisfied;
                    result = SolutionQuality.Unfeasible;
                }
                else
                {
                    errorCode = SolverErrorCode.NotEnoughResourcesToAllocateAllInstances;
                    result = SolutionQuality.Partial;
                }
            }
            else
            {
                result = SolutionQuality.Unfeasible;
            }

            this.stopwatch.Stop();
            AllocationPlan allocationPlan = this.GetAllocationPlan();

            score = ComputeVariance(this.Rows, this.Columns, this.ResourceCount, this.variables, this.containerCoefficientsPerResource, this.machineConstraintsPerResource, this.averageAvailableResource, this.resourceWeight);

            return new SolverResult(
                result,
                errorCode,
                baselineScore,
                initialStateScore,
                score,
                iterativeSwappingIterations,
                fullScanIterations,
                interContainerIterations,
                this.stopwatch.Elapsed,
                this.variables,
                allocationPlan.Allocations,
                allocationPlan.NewAllocations,
                allocationPlan.Deallocations,
                initialGuessMetrics,
                solutionMetrics);
        }

        internal static double ComputeVariance(int rows, int columns, int resourceCount, byte[][] matrixVariables, int[][] containerResourceCoefficients, int[][] machineResourceConstraints, double[] averageAvailableResources, double[] resourceWeights)
        {
            double variance = 0.0;
            for (int k = 0; k < resourceCount; k++)
            {
                double squaredSum = 0.0;
                for (int i = 0; i < rows; i++)
                {
                    double sumUsed = 0.0;
                    for (int j = 0; j < columns; j++)
                    {
                        sumUsed += matrixVariables[i][j] * containerResourceCoefficients[k][j];
                    }

                    squaredSum += Math.Pow(machineResourceConstraints[k][i] - sumUsed - averageAvailableResources[k], 2);
                }

                double sampleStandardVariance = Math.Sqrt(squaredSum / (rows - 1));
                variance += sampleStandardVariance / averageAvailableResources[k] * resourceWeights[k];
            }

            return variance;
        }

        private static int[] CalculateMaxDeallocationCounts(decimal allowedChurnPercentage, int[] desiredInstanceCounts, int[] currentAllocationsCounts)
        {
            int[] maxDeallocationCounts = new int[desiredInstanceCounts.Length];

            if (currentAllocationsCounts != null && currentAllocationsCounts.Length > 0)
            {
                for (int j = 0; j < desiredInstanceCounts.Length; j++)
                {
                    maxDeallocationCounts[j] = (int)(allowedChurnPercentage * currentAllocationsCounts[j]);
                }
            }
            else
            {
                for (int j = 0; j < desiredInstanceCounts.Length; j++)
                {
                    maxDeallocationCounts[j] = (int)(allowedChurnPercentage * desiredInstanceCounts[j]);
                }
            }

            return maxDeallocationCounts;
        }

        private static int GetTotalResourceRequirement(ResourceKind resource, ContainerSpec[] containersWithResourceToMaximize)
        {
            return containersWithResourceToMaximize.Select(c => c.ResourceUsages.Where(x => x.Kind == resource).Sum(x => x.Value)).Sum();
        }

        private static int GetTotalResourceAmount(ResourceKind resource, Machine[] machinesWithResourceToMaximize)
        {
            return machinesWithResourceToMaximize.Select(c => c.AvailableResources.Where(x => x.Kind == resource).Sum(x => x.Value)).Sum();
        }

        private bool IsExceedChurnPercentContainer(int containerSpecIndex)
        {
            return !this.parameters.CurrentAllocations.IsNullOrEmpty()
                && this.parameters.AllowedChurnPercentage.HasValue
                && this.maxDeallocationCount != null
                && this.currentDeallocationCount != null
                && this.currentDeallocationCount[containerSpecIndex] >= this.maxDeallocationCount[containerSpecIndex];
        }

        private void ComputeAverageAvailableResource()
        {
            for (int k = 0; k < this.ResourceCount; k++)
            {
                double totalResource = 0.0;
                for (int i = 0; i < this.Rows; i++)
                {
                    totalResource += this.machineConstraintsPerResource[k][i];
                }

                double desiredResource = 0.0;
                for (int i = 0; i < this.Columns; i++)
                {
                    desiredResource += this.desiredContainerInstanceCounts[i] * this.containerCoefficientsPerResource[k][i];
                }

                this.averageAvailableResource[k] = (totalResource - desiredResource) / this.Rows;
            }
        }

        private double ComputePartialVarianceCoefficient(int bestScoreIndex, int worstScoreIndex)
        {
            double results = 0.0;
            for (int k = 0; k < this.ResourceCount; k++)
            {
                double partialVariance = Math.Pow(Math.Abs(this.ComputeMachineScore(bestScoreIndex, k) - this.averageAvailableResource[k]), 2)
                                        + Math.Pow(Math.Abs(this.ComputeMachineScore(worstScoreIndex, k) - this.averageAvailableResource[k]), 2);
                double partialStd = Math.Sqrt(partialVariance / 2);

                double partialVarianceCoefficient = partialStd / Math.Abs(this.averageAvailableResource[k]);
                results += partialVarianceCoefficient * this.resourceWeight[k];
            }

            return results;
        }

        private double ComputePairwiseOverallocationRatioStandardDeviation(int bestScoreIndex, int worstScoreIndex)
        {
            double results = 0.0;
            for (int k = 0; k < this.ResourceCount; k++)
            {
                double overallocationRatio1 = 0, overallocationRatio2 = 0;
                if (this.ComputeMachineScore(bestScoreIndex, k) < 0)
                {
                    overallocationRatio1 = Math.Abs(this.ComputeMachineScore(bestScoreIndex, k)) / this.machineConstraintsPerResource[k][bestScoreIndex];
                }

                if (this.ComputeMachineScore(worstScoreIndex, k) < 0)
                {
                    overallocationRatio2 = Math.Abs(this.ComputeMachineScore(worstScoreIndex, k)) / this.machineConstraintsPerResource[k][worstScoreIndex];
                }

                double avg = (overallocationRatio1 + overallocationRatio2) / 2;

                double overallocationRatioVar = Math.Pow(overallocationRatio1 - avg, 2) + Math.Pow(overallocationRatio2 - avg, 2);
                double overallocationRatioStd = Math.Sqrt(overallocationRatioVar / 2);
                results += overallocationRatioStd;
            }

            return results;
        }

        private bool IsMachinePairOverAllocated(int machineIdx1, int machineIdx2)
        {
            bool result = !this.AreMachineConstraintsSatisfied(machineIdx1) || !this.AreMachineConstraintsSatisfied(machineIdx2);
            return result;
        }

        private void IdentifyHardConstraint(ResourceKind hardConstraintResource, ResourceKind[] resources)
        {
            int index = Array.IndexOf(resources, hardConstraintResource);
            this.hardConstraintResourceIndex = index >= 0 ? index : this.hardConstraintResourceIndex;
        }

        private void AssignInstancesToMaximizeResourceUtilization()
        {
            Machine[] machinesWithResourceToMaximize = this.parameters.Machines
                .Where(c => c.AvailableResources.Any(x => x.Requires(this.parameters.MaximizeResourceUsageFor.Value)))
                .ToArray();

            ContainerSpec[] containersWithResourceToMaximize = this.parameters.ContainerSpecs
                .Where(c => c.ResourceUsages.Any(x => x.Requires(this.parameters.MaximizeResourceUsageFor.Value)))
                .ToArray();

            int numerator = GetTotalResourceAmount(this.parameters.MaximizeResourceUsageFor.Value, machinesWithResourceToMaximize);

            int denominator0 = GetTotalResourceRequirement(this.parameters.MaximizeResourceUsageFor.Value, containersWithResourceToMaximize);

            if (denominator0 == 0)
            {
                return;
            }

            int approximateInstances = numerator / denominator0;

            Dictionary<string, ContainerSpec> excluded = new Dictionary<string, ContainerSpec>();
            foreach (ContainerSpec container in containersWithResourceToMaximize)
            {
                if (container.DesiredInstanceCount > approximateInstances)
                {
                    excluded.Add(container.Name, container);
                }
            }

            int resourceAmountForAllExcludedContainerInstances = excluded.Sum(c => c.Value.ResourceUsages.Where(ru => ru.Kind == this.parameters.MaximizeResourceUsageFor.Value).Select(ru => ru.Value * c.Value.DesiredInstanceCount).Sum());
            int totalResourceAmount = GetTotalResourceAmount(this.parameters.MaximizeResourceUsageFor.Value, machinesWithResourceToMaximize) - resourceAmountForAllExcludedContainerInstances;

            int resourceAmountForExcludedContainerTypes = excluded.Sum(c => c.Value.ResourceUsages.Where(ru => ru.Kind == this.parameters.MaximizeResourceUsageFor.Value).Select(ru => ru.Value).Sum());
            int denominator = GetTotalResourceRequirement(this.parameters.MaximizeResourceUsageFor.Value, containersWithResourceToMaximize) - resourceAmountForExcludedContainerTypes;

            if (denominator == 0)
            {
                return;
            }

            int instancesPerContainer = totalResourceAmount / denominator;

            int remainingResourceAmount = totalResourceAmount % denominator;

            for (int i = 0; i < this.desiredContainerInstanceCounts.Length; i++)
            {
                ContainerSpec container = this.parameters.ContainerSpecs[i];
                if (container.ResourceUsages.Any(c => c.Requires(this.parameters.MaximizeResourceUsageFor.Value)))
                {
                    if (!excluded.ContainsKey(container.Name))
                    {
                        int instances = instancesPerContainer;

                        int resourcePerContainer = container.ResourceUsages.First(c => c.Requires(this.parameters.MaximizeResourceUsageFor.Value)).Value;

                        if (remainingResourceAmount >= resourcePerContainer)
                        {
                            instances++;
                            remainingResourceAmount -= resourcePerContainer;
                        }

                        this.desiredContainerInstanceCounts[i] = instances;
                    }
                }
            }
        }

        private bool EnsureSolutionIsViable(ref SolverErrorCode optionalErrorCode)
        {
            foreach (int desiredInstanceCount in this.desiredContainerInstanceCounts)
            {
                for (int k = 0; k < this.ResourceCount; k++)
                {
                    if (desiredInstanceCount > this.machineConstraintsPerResource[k].Count(c => c > 0))
                    {
                        optionalErrorCode = SolverErrorCode.ContainerRequiresAtLeastOneResourceThatIsNotAvailable;
                    }

                    if (desiredInstanceCount > this.machineConstraintsPerResource[k].Length)
                    {
                        optionalErrorCode = SolverErrorCode.MoreContainerInstancesThanMachinesAvailable;
                        return false;
                    }
                }
            }

            return true;
        }

        private byte[][] InitializeMatrix()
        {
            byte[][] matrix = new byte[this.Rows][];

            for (int i = 0; i < this.Rows; i++)
            {
                matrix[i] = new byte[this.Columns];
            }

            return matrix;
        }

        private byte[][] PopulateInitialState()
        {
            if (this.parameters.CurrentAllocations != null && this.parameters.CurrentAllocations.Length != 0)
            {
                ILookup<string, string> allocationByMachine = this.parameters.CurrentAllocations
                    .ToLookup(c => c.Machine.Name, d => d.ContainerSpec.Name);

                for (int i = 0; i < this.Rows; i++)
                {
                    string machine = this.parameters.Machines[i].Name;
                    IDictionary<string, string> containersByName = allocationByMachine[machine].ToDictionary(c => c);

                    for (int j = 0; j < this.Columns; j++)
                    {
                        ContainerSpec container = this.parameters.ContainerSpecs[j];

                        if (containersByName.ContainsKey(container.Name))
                        {
                            this.initialState[i][j] = 1;
                        }
                    }
                }
            }

            return this.initialState;
        }

        private AllocationPlan GetAllocationPlan()
        {
            List<Allocation> allocations = new List<Allocation>(), newAllocations = new List<Allocation>(), deallocations = new List<Allocation>();

            for (int i = 0; i < this.Rows; i++)
            {
                for (int j = 0; j < this.Columns; j++)
                {
                    byte before = this.initialState[i][j];

                    byte after = this.variables[i][j];

                    Allocation item = new Allocation
                    {
                        Machine = this.parameters.Machines[i],
                        ContainerSpec = this.parameters.ContainerSpecs[j],
                    };

                    if (after == 1)
                    {
                        allocations.Add(item);
                    }

                    if (before != after)
                    {
                        if (after == 1)
                        {
                            newAllocations.Add(item);
                        }

                        if (before == 1)
                        {
                            deallocations.Add(item);
                        }
                    }
                }
            }

            return new AllocationPlan(allocations, newAllocations, deallocations);
        }

        private int[] GetDeallocationCount()
        {
            int[] counts = new int[this.Columns];
            for (int i = 0; i < this.Rows; i++)
            {
                for (int j = 0; j < this.Columns; j++)
                {
                    byte before = this.initialState[i][j];
                    byte after = this.variables[i][j];

                    if (before != after && before == 1)
                    {
                        counts[j]++;
                    }
                }
            }

            return counts;
        }

        private int IterativeSwapBestWorstMachinesForContainers(int maxIterations)
        {
            double[] machineScores = new double[this.variables.Length];
            for (int i = 0; i < machineScores.Length; i++)
            {
                double machineScore = this.ComputeMachineScore(i);
                machineScores[i] = machineScore;
            }

            int previousWorstScoreIndex = -1;
            int previousBestScoreIndex = -1;

            int iteration = 0;
            while (!this.ShouldStop() && iteration++ < maxIterations)
            {
                int worstScoreIndex = machineScores.GetMinIndex();
                int bestScoreIndex = machineScores.GetMaxIndex();

                if (previousBestScoreIndex == bestScoreIndex && previousWorstScoreIndex == worstScoreIndex)
                {
                    break;
                }

                double baselineScore = this.ComputePartialVarianceCoefficient(bestScoreIndex, worstScoreIndex);
                double overallocatedPercentage = this.ComputePairwiseOverallocationRatioStandardDeviation(bestScoreIndex, worstScoreIndex);
                bool overloaded = this.IsMachinePairOverAllocated(bestScoreIndex, worstScoreIndex);

                for (int j = 0; j < this.Columns; j++)
                {
                    if (this.variables[bestScoreIndex][j] == 1 || this.IsExceedChurnPercentContainer(j))
                    {
                        continue;
                    }

                    if (this.variables[worstScoreIndex][j] == 1)
                    {
                        bool isSwap = this.TrySwap(baselineScore, overallocatedPercentage, overloaded, j, worstScoreIndex, bestScoreIndex, machineScores, out baselineScore);
                    }
                }

                previousBestScoreIndex = bestScoreIndex;
                previousWorstScoreIndex = worstScoreIndex;
            }

            return iteration;
        }

        private int PerformFullScanSwapping(int maxIterations)
        {
            double[] scores = new double[this.variables.Length];

            for (int i = 0; i < scores.Length; i++)
            {
                double score = this.ComputeMachineScore(i);
                scores[i] = score;
            }

            int iteration;

            bool isExistSwap = true;
            for (iteration = 0; !this.ShouldStop() && isExistSwap && iteration < maxIterations; iteration++)
            {
                isExistSwap = false;
                for (int i = 0; !this.ShouldStop() && i < this.Rows; i++)
                {
                    for (int i2 = this.Rows - 1; i2 >= 0; i2--)
                    {
                        if (i == i2)
                        {
                            continue;
                        }

                        int worstScoreIndex = i;
                        int bestScoreIndex = i2;

                        double baselineScore = this.ComputePartialVarianceCoefficient(bestScoreIndex, worstScoreIndex);
                        double overallocatedPercentage = this.ComputePairwiseOverallocationRatioStandardDeviation(bestScoreIndex, worstScoreIndex);
                        bool overloaded = this.IsMachinePairOverAllocated(bestScoreIndex, worstScoreIndex);

                        for (int j = 0; j < this.Columns; j++)
                        {
                            if (this.variables[bestScoreIndex][j] == 1 || this.IsExceedChurnPercentContainer(j))
                            {
                                continue;
                            }

                            if (this.variables[worstScoreIndex][j] == 1)
                            {
                                bool isSuccess = this.TrySwap(
                                    baselineScore,
                                    overallocatedPercentage,
                                    overloaded,
                                    j,
                                    worstScoreIndex,
                                    bestScoreIndex,
                                    scores,
                                    out baselineScore);
                                if (isSuccess)
                                {
                                    isExistSwap = true;
                                }
                            }
                        }
                    }
                }
            }

            return iteration;
        }

        private int FullScanSwappingContainers(int maxIterations)
        {
            int iteration;
            bool isExistSwap = true;
            for (iteration = 0; !this.ShouldStop() && isExistSwap && iteration < maxIterations; iteration++)
            {
                isExistSwap = false;

                for (int i = 0; !this.ShouldStop() && i < this.Rows; i++)
                {
                    for (int i2 = this.Rows - 1; i2 >= 0; i2--)
                    {
                        if (i == i2)
                        {
                            continue;
                        }

                        int machine1 = i;
                        int machine2 = i2;

                        double baselineScore = this.ComputePartialVarianceCoefficient(machine1, machine2);
                        double overallocatedPercentage = this.ComputePairwiseOverallocationRatioStandardDeviation(machine1, machine2);
                        bool overloaded = this.IsMachinePairOverAllocated(machine1, machine2);

                        for (int container1 = 0; container1 < this.Columns; container1++)
                        {
                            if (this.variables[machine1][container1] == 0 || this.IsExceedChurnPercentContainer(container1))
                            {
                                continue;
                            }

                            for (int container2 = 0; container2 < this.Columns; container2++)
                            {
                                if (this.variables[machine2][container2] == 0 || container2 == container1 ||
                                    this.IsExceedChurnPercentContainer(container2))
                                {
                                    continue;
                                }

                                if (this.variables[machine1][container2] == 1 || this.variables[machine2][container1] == 1)
                                {
                                    continue;
                                }

                                this.variables[machine1][container1] = 0;
                                this.variables[machine1][container2] = 1;
                                this.variables[machine2][container1] = 1;
                                this.variables[machine2][container2] = 0;

                                double newScore = this.ComputePartialVarianceCoefficient(machine1, machine2);
                                double newOverallocatedPercentage = this.ComputePairwiseOverallocationRatioStandardDeviation(machine1, machine2);
                                bool newOverloaded = this.IsMachinePairOverAllocated(machine1, machine2);

                                bool isThisSwap = this.ShouldUpdatePlan(baselineScore, overallocatedPercentage, overloaded, newScore, newOverallocatedPercentage, newOverloaded);

                                if (isThisSwap)
                                {
                                    baselineScore = newScore;
                                    isExistSwap = true;
                                    this.currentDeallocationCount[container1]++;
                                    this.currentDeallocationCount[container2]++;
                                }
                                else
                                {
                                    this.variables[machine1][container1] = 1;
                                    this.variables[machine1][container2] = 0;
                                    this.variables[machine2][container1] = 0;
                                    this.variables[machine2][container2] = 1;
                                }
                            }
                        }
                    }
                }
            }

            return iteration;
        }

        private bool ShouldUpdatePlan(double baselineScore, double baselineOverallocatedPercentage, bool isBaselineOverloaded, double newScore, double newOverallocatedPercentage, bool isNewOverloaded)
        {
            if (isBaselineOverloaded && !isNewOverloaded)
            {
                return true;
            }

            if (!isBaselineOverloaded && isNewOverloaded)
            {
                return false;
            }

            if (!isBaselineOverloaded && !isNewOverloaded)
            {
                return newScore < baselineScore;
            }

            if (Math.Abs(baselineOverallocatedPercentage - newOverallocatedPercentage) > MplsAllocationSolverImpl.PseudoThreshold)
            {
                return newOverallocatedPercentage < baselineOverallocatedPercentage;
            }

            return newScore < baselineScore;
        }

        private bool AreHardConstraintsSatisfied()
        {
            bool result = true;
            if (this.hardConstraintResourceIndex.HasValue)
            {
                int k = this.hardConstraintResourceIndex.Value;
                if (!this.AreRowsLessOrEqualThan(this.containerCoefficientsPerResource[k], this.machineConstraintsPerResource[k]))
                {
                    result = false;
                }
            }

            return result;
        }

        private bool IsHardConstraintSatisfy(int machineIndex)
        {
            bool result = true;

            if (this.hardConstraintResourceIndex.HasValue)
            {
                int k = this.hardConstraintResourceIndex.Value;
                if (!this.IsRowLessOrEqualThan(this.containerCoefficientsPerResource[k], this.machineConstraintsPerResource[k][machineIndex], machineIndex))
                {
                    result = false;
                }
            }

            return result;
        }

        private bool AreMachineConstraintsSatisfied(int machineIndex)
        {
            bool result = true;

            for (int k = 0; k < this.ResourceCount; k++)
            {
                if (!this.IsRowLessOrEqualThan(this.containerCoefficientsPerResource[k], this.machineConstraintsPerResource[k][machineIndex], machineIndex))
                {
                    result = false;
                }
            }

            return result;
        }

        private bool TrySwap(
            double baselineScore,
            double overallocatedPercentage,
            bool isOverloaded,
            int containerSpecIndex,
            int worstScoreIndex,
            int bestScoreIndex,
            double[] scores,
            out double score)
        {
            bool swapped = false;
            score = baselineScore;

            if (!this.IsHardConstraintSatisfy(bestScoreIndex))
            {
                return swapped;
            }

            this.variables[worstScoreIndex][containerSpecIndex] = 0;
            this.variables[bestScoreIndex][containerSpecIndex] = 1;

            double newScore = this.ComputePartialVarianceCoefficient(bestScoreIndex, worstScoreIndex);
            double newOverallocatedPercentage = this.ComputePairwiseOverallocationRatioStandardDeviation(bestScoreIndex, worstScoreIndex);
            bool isNewOverloaded = this.IsMachinePairOverAllocated(bestScoreIndex, worstScoreIndex);

            swapped = this.ShouldUpdatePlan(score, overallocatedPercentage, isOverloaded, newScore, newOverallocatedPercentage, isNewOverloaded);

            if (swapped)
            {
                score = newScore;

                scores[worstScoreIndex] = this.ComputeMachineScore(worstScoreIndex);
                scores[bestScoreIndex] = this.ComputeMachineScore(bestScoreIndex);

                this.currentDeallocationCount[containerSpecIndex]++;
            }
            else
            {
                this.variables[worstScoreIndex][containerSpecIndex] = 1;
                this.variables[bestScoreIndex][containerSpecIndex] = 0;
            }

            return swapped;
        }

        private double CombineResourceScores(params double[] scores)
        {
            return scores.Sum();
        }

        private byte[][] GenerateSamples(int iterations = 10)
        {
            byte[][] result = null;
            double baselineScore = double.MaxValue;

            for (int i = 0; i < iterations; i++)
            {
                if (this.ShouldStop())
                {
                    break;
                }

                this.variables = this.GenerateSample();
                double newScore = ComputeVariance(this.Rows, this.Columns, this.ResourceCount, this.variables, this.containerCoefficientsPerResource, this.machineConstraintsPerResource, this.averageAvailableResource, this.resourceWeight);

                if (newScore < baselineScore)
                {
                    baselineScore = newScore;
                    result = this.variables;
                }
            }

            return result;
        }

        private byte[][] GenerateInitialAllocation()
        {
            this.variables = this.InitializeMatrix();

            Tuple<double, int>[] machinesAndScores = new Tuple<double, int>[this.Rows];
            for (int i = 0; i < this.Rows; i++)
            {
                double score = this.ComputeMachineScore(i);
                machinesAndScores[i] = new Tuple<double, int>(score, i);
            }

            Tuple<double, int>[] ordered = machinesAndScores.OrderByDescending(c => c.Item1).ToArray();

            for (int j = 0; j < this.Columns; j++)
            {
                foreach (Tuple<double, int> machineAndScore in ordered.Take(this.desiredContainerInstanceCounts[j]))
                {
                    this.variables[machineAndScore.Item2][j] = 1;
                }

                for (int i = 0; i < this.Rows; i++)
                {
                    double score = this.ComputeMachineScore(i);
                    machinesAndScores[i] = new Tuple<double, int>(score, i);
                }

                ordered = machinesAndScores.OrderByDescending(c => c.Item1).ToArray();
            }

            return this.variables;
        }

        private byte[][] GenerateSample()
        {
            byte[][] sample = new byte[this.Columns][];

            for (int j = 0; j < this.Columns; j++)
            {
                int numberOfOnes = this.desiredContainerInstanceCounts[j];
                byte[] array = RandomBinary.GenerateRandomBinaryArray(this.Rows, numberOfOnes);
                sample[j] = array;
            }

            return sample.Rotate();
        }

        private byte[][] RandomPlacementOverInitialState(byte[][] initialMatrix)
        {
            byte[][] sample = initialMatrix.Rotate();

            for (int j = 0; j < this.Columns; j++)
            {
                int numberOfOnes = this.desiredContainerInstanceCounts[j];
                byte[] array = RandomBinary.GenerateRandomBinaryArray(sample[j], numberOfOnes);
                sample[j] = array;
            }

            return sample.Rotate();
        }

        private byte[][] GreedyPlacementOverInitialState(byte[][] initialMatrix)
        {
            byte[][] sample = initialMatrix.Rotate();

            for (int j = 0; j < this.Columns; j++)
            {
                int desiredNumOfOnes = this.desiredContainerInstanceCounts[j];
                int currentNumOfOnes = 0;
                for (int i = 0; i < sample[j].Length; i++)
                {
                    if (sample[j][i] == 1)
                    {
                        currentNumOfOnes++;
                    }
                }

                while (desiredNumOfOnes > currentNumOfOnes)
                {
                    int machineIndex = -1;
                    double score = -1;
                    for (int i = 0; i < sample[j].Length; i++)
                    {
                        if (sample[j][i] == 1)
                        {
                            continue;
                        }

                        double currentScore = this.ComputeMachineScore(i);
                        if (machineIndex == -1 || currentScore > score)
                        {
                            machineIndex = i;
                            score = currentScore;
                        }
                    }

                    if (machineIndex != -1)
                    {
                        sample[j][machineIndex] = 1;
                        currentNumOfOnes++;
                    }
                }

            }

            return sample.Rotate();
        }

        private double ComputeMachineScore(int machineIndex)
        {
            double[] scores = new double[this.ResourceCount];

            for (int k = 0; k < this.ResourceCount; k++)
            {
                scores[k] = this.ComputeMachineScore(machineIndex, k);
            }

            double result = this.CombineResourceScores(scores);
            return result;
        }

        private double ComputeMachineScore(int machineIndex, int resourceIndex)
        {
            int[] coefficients = this.containerCoefficientsPerResource[resourceIndex];
            int constraint = this.machineConstraintsPerResource[resourceIndex][machineIndex];
            return this.ComputeMachineScore(machineIndex, coefficients, constraint);
        }

        private double ComputeMachineScore(int machineIndex, int[] coefficients, int constraint)
        {
            int sum = this.SumRow(coefficients, machineIndex);
            double difference = constraint - sum;
            return difference;
        }

        private Dictionary<ResourceKind, AllocationMetric[]> CaptureMetrics()
        {
            Dictionary<ResourceKind, AllocationMetric[]> metricsPerResource = new Dictionary<ResourceKind, AllocationMetric[]>();
            for (int k = 0; k < this.ResourceCount; k++)
            {
                AllocationMetric[] metrics = new AllocationMetric[this.Rows];
                ResourceKind resourceKind = this.parameters.OptimizeResources[k];
                metricsPerResource[resourceKind] = metrics;

                for (int i = 0; i < metrics.Length; i++)
                {
                    Machine machine = this.parameters.Machines[i];
                    metrics[i] = new AllocationMetric
                    {
                        Index = i,
                        MachineName = machine.Name,
                        Resource = resourceKind,
                        Allocated = this.SumRow(this.containerCoefficientsPerResource[k], i),
                        TotalAvailable = this.machineConstraintsPerResource[k][i]
                    };
                }
            }

            return metricsPerResource;
        }

        private bool AllContainerInstanceCountSatisfied()
        {
            bool result = this.AreColumnsEqualTo(this.desiredContainerInstanceCounts);
            return result;
        }

        private bool AllConstraintsAreSatisfied()
        {
            bool result = true;

            for (int k = 0; k < this.ResourceCount; k++)
            {
                if (!this.AreRowsLessOrEqualThan(this.containerCoefficientsPerResource[k], this.machineConstraintsPerResource[k]))
                {
                    result = false;
                    break;
                }
            }

            return result;
        }

        private bool AreRowsLessOrEqualThan(int[] coefficients, int[] constraints)
        {
            for (int i = 0; i < this.Rows; i++)
            {
                if (!this.IsRowLessOrEqualThan(coefficients, constraints[i], i))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsRowLessOrEqualThan(int[] coefficients, double constraint, int rowIndex)
        {
            int sum = this.SumRow(coefficients, rowIndex);
            return sum <= constraint;
        }

        private bool AreColumnsEqualTo(int[] constraints)
        {
            for (int j = 0; j < this.Columns; j++)
            {
                if (!this.IsColumnEqualTo(constraints[j], j))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsColumnEqualTo(int value, int columnIndex)
        {
            int sum = this.SumColumn(columnIndex);
            return sum == value;
        }

        private int SumRow(int[] coefficients, int rowIndex)
        {
            int sum = 0;
            for (int j = 0; j < this.Columns; j++)
            {
                int product = this.variables[rowIndex][j] * coefficients[j];
                sum += product;
            }

            return sum;
        }

        private int SumColumn(int columnIndex)
        {
            int sum = 0;
            for (int i = 0; i < this.Rows; i++)
            {
                sum += this.variables[i][columnIndex];
            }

            return sum;
        }

        private bool ShouldStop()
        {
            bool result = this.parameters.Timeout.HasValue && this.stopwatch.ElapsedTicks > this.parameters.Timeout.Value.Ticks;

            if (this.parameters.AllowedChurnPercentage.HasValue
                && !this.parameters.CurrentAllocations.IsNullOrEmpty()
                && this.HasExceededDeallocationThreshold())
            {
                this.currentDeallocationCount = this.GetDeallocationCount();
                if (this.HasExceededDeallocationThreshold())
                {
                    result = true;
                }
            }

            return result;
        }

        private bool HasExceededDeallocationThreshold()
        {
            int containersNum = 0;
            for (int i = 0; i < this.Columns; i++)
            {
                if (this.IsExceedChurnPercentContainer(i))
                {
                    containersNum++;
                }
                else
                {
                    break;
                }
            }

            if (containersNum >= this.Columns)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private int[] GetInitialAllocationCounts()
        {
            int[] currentAllocationsCounts = new int[this.Columns];

            for (int j = 0; j < this.Columns; j++)
            {
                string containerSpecName = this.parameters.ContainerSpecs[j].Name;
                currentAllocationsCounts[j] = this.parameters.CurrentAllocations.Count(c => c.ContainerSpec.Name == containerSpecName);
            }

            return currentAllocationsCounts;
        }
    }
}