namespace Mpls.SolverContract
{
    using System;
    using System.Collections.Generic;
    using Mpls.Model;

    public class SolverResult
    {
        public SolverResult(
            SolutionQuality solutionQuality,
            SolverErrorCode? errorCode,
            double baselineScore,
            double initialStateScore,
            double score,
            int iterationsSpent,
            int fullScanIterationsSpent,
            int interContainerIterationsSpent,
            TimeSpan executionTimeElapsed,
            byte[][] solutionMatrix,
            IList<Allocation> allocations,
            IList<Allocation> newAllocations,
            IList<Allocation> deallocations,
            Dictionary<ResourceKind, AllocationMetric[]> initialGuessMetrics,
            Dictionary<ResourceKind, AllocationMetric[]> solutionMetrics)
        {
            this.Variables = solutionMatrix;
            this.Score = score;
            this.ExecutionTimeElapsed = executionTimeElapsed;
            this.SolutionQuality = solutionQuality;
            this.ErrorCode = errorCode;
            this.Allocations = new List<Allocation>(allocations);
            this.NewAllocations = new List<Allocation>(newAllocations);
            this.Deallocations = new List<Allocation>(deallocations);
            this.InitialAllocationMetrics = initialGuessMetrics;
            this.SolutionMetrics = solutionMetrics;
            this.BaselineScore = baselineScore;
            this.InitialStateScore = initialStateScore;
            this.FullScanIterationsSpent = fullScanIterationsSpent;
            this.BestWorstSwappingIterationsSpent = iterationsSpent;
            this.InterContainerSwappingIterationsSpent = interContainerIterationsSpent;
        }

        public SolutionQuality SolutionQuality { get; }

        public SolverErrorCode? ErrorCode { get; }

        public IReadOnlyCollection<Allocation> Allocations { get; }

        public IReadOnlyCollection<Allocation> NewAllocations { get; }

        public IReadOnlyCollection<Allocation> Deallocations { get; }

        public double BaselineScore { get; }

        public double InitialStateScore { get; }

        public double Score { get; }

        public int BestWorstSwappingIterationsSpent { get; }

        public int InterContainerSwappingIterationsSpent { get; }

        public int FullScanIterationsSpent { get; }

        public TimeSpan ExecutionTimeElapsed { get; }

        public byte[][] Variables { get; }

        public IDictionary<ResourceKind, AllocationMetric[]> InitialAllocationMetrics { get; }

        public IDictionary<ResourceKind, AllocationMetric[]> SolutionMetrics { get; }

    }
}