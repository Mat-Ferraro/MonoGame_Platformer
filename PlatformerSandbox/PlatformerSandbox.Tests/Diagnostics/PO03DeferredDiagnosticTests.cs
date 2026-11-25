using System;
using System.Collections.Generic;
using PlatformerSandbox.Diagnostics;
using Xunit;

namespace PlatformerSandbox.Tests.Diagnostics;

public sealed class PO03DeferredDiagnosticTests
{
    [Fact]
    public void FormattingIsDeferredAndFailedAttemptRetainsCommittedContext()
    {
        var recorder = new DiagnosticContextRecorder(2);
        int calls = 0;
        recorder.UpdateCommittedState(42, value => { calls++; return new Dictionary<string,string> { ["tick"] = value.ToString() }; });
        recorder.BeginOperation("world.step", 43, value => { calls++; return $"attempt={value}"; }, 43);
        Assert.Equal(0, calls);
        var snapshot = recorder.Snapshot();
        Assert.Equal(2, calls);
        Assert.Equal("42", snapshot.LastCommittedState["tick"]);
        Assert.Equal("attempt=43", snapshot.PendingOperation!.Detail);
        recorder.CommitOperation(43, static v => $"committed={v}");
        recorder.RecordActivity("event", 44, static v => v.ToString(), 44);
        recorder.RecordActivity("event", 45, static v => v.ToString(), 45);
        var next = recorder.Snapshot();
        Assert.Null(next.PendingOperation);
        Assert.Equal(2, next.RecentActivity.Count);
        Assert.Equal("44", next.RecentActivity[0].Detail);
        Assert.Equal("45", next.RecentActivity[1].Detail);
        recorder.UpdateCommittedState(new Dictionary<string,string> { ["scene"] = "next" });
        Assert.False(recorder.Snapshot().LastCommittedState.ContainsKey("tick"));
    }
}
