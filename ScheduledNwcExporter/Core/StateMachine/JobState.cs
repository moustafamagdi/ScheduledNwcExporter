namespace ScheduledNwcExporter.Core.StateMachine
{
    public enum ExportJobState
    {
        Idle,
        WaitingForSchedule,
        ScheduleTriggered,
        InitializingJob,
        ValidatingModel,
        OpeningModel,
        ModelOpened,
        PreparingModel,
        ValidatingWorksets,
        ValidatingLinks,
        PreparingExport,
        Exporting,
        VerifyingOutput,
        ClosingModel,
        Completed,
        Failed,
        Retrying,
        QueueCompleted
    }
}
