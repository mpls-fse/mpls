namespace Mpls.SolverContract
{
    public enum SolverErrorCode
    {
        None,

        AdjustingErrorNegativeInfinityOrNaN,

        MoreContainerInstancesThanMachinesAvailable,

        ContainerRequiresAtLeastOneResourceThatIsNotAvailable,

        NotEnoughResourcesToAllocateAllInstances,

        HardConstraintUnsatisfied,
    }
}