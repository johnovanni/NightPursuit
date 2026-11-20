using NightPursuit.Core.Session;

namespace NightPursuit.Core.Tests;

public class ReleaseFirstGateTests
{
    [Fact]
    public void Idle_gate_does_not_wait()
    {
        var gate = new ReleaseFirstGate();
        Assert.False(gate.Waiting);
        gate.Update(true);
        Assert.False(gate.Waiting);
    }

    [Fact]
    public void Waits_while_a_confirm_control_stays_held()
    {
        var gate = new ReleaseFirstGate();
        gate.Begin();
        gate.Update(true);
        gate.Update(true);
        Assert.True(gate.Waiting);
    }

    [Fact]
    public void One_release_ends_the_wait_for_good()
    {
        var gate = new ReleaseFirstGate();
        gate.Begin();
        gate.Update(true);
        gate.Update(false);
        Assert.False(gate.Waiting);
        gate.Update(true);   // pressing again is a real confirm
        Assert.False(gate.Waiting);
    }

    [Fact]
    public void Nothing_held_when_the_screen_appears_ends_the_wait_on_the_next_frame()
    {
        var gate = new ReleaseFirstGate();
        gate.Begin();
        gate.Update(false);
        Assert.False(gate.Waiting);
    }

    [Fact]
    public void Begin_again_waits_again()
    {
        var gate = new ReleaseFirstGate();
        gate.Begin();
        gate.Update(false);
        gate.Begin();
        Assert.True(gate.Waiting);
    }
}
