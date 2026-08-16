# Revit 2024 ExternalEvent Execution Model

## Root cause of the failed model open

The execution log from 2026-08-16 showed that `Application.OpenDocumentFile` failed immediately on every retry with Revit's generic `InternalException`. The source of the failure was the previous UI path, which used `Task.Run` to call the queue processor. That background task then called `Application.OpenDocumentFile`, `Document.Export`, and other Revit API methods outside a valid Revit API context.

Revit API access is thread-affine. A modeless WPF window may not directly call the Revit API and must not move Revit API work to a worker thread. Autodesk's External Events framework exists for this exact scenario: the modeless UI raises an `ExternalEvent`, and Revit invokes the registered `IExternalEventHandler.Execute(UIApplication)` during an available Idling cycle.[1]

## Revised flow

```text
Modeless WPF window
        |
        | User selects Run Now / schedule reaches its time
        v
ExportQueueExternalEventHandler.Start()
        |
        | ExternalEvent.Raise()
        v
Revit invokes IExternalEventHandler.Execute()
        |
        | one model job: open detached -> verify worksets -> inspect links
        | -> export NWC -> verify output -> close document
        v
WPF dispatcher queues the next ExternalEvent only after Execute returns
        |
        v
Next model or session summary
```

Each `Execute` invocation processes **one** model job. This preserves failure isolation while ensuring `OpenDocumentFile`, `Document.Export`, workset queries, link queries, and document closure occur only in a valid Revit-owned API context. The UI remains modeless and queue cancellation is cooperative: an active export is allowed to reach a safe boundary, then no further model begins.

## Operational limitations

The NWC export call itself is synchronous. Revit does not expose a safe, supported API to interrupt that call midway. The Cancel/Pause control therefore requests cancellation and stops the queue before the next model begins after the active operation returns.

An export session requires Revit to stay running with the add-in loaded. This implementation does not attempt unsupported headless Revit execution or background process startup.

## Reference

[1] [Autodesk, External Events](https://help.autodesk.com/cloudhelp/2018/ENU/Revit-API/Revit_API_Developers_Guide/Advanced_Topics/External_Events.html)
