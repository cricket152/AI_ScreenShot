using ScreenShot.App;

internal static class PanelMoveTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        Check(!PanelMovePolicy.ReachedThreshold(0,0,1),"A background click does not move the panel");
        Check(!PanelMovePolicy.ReachedThreshold(3,-3,1),"Movement below four logical pixels does not start dragging");
        Check(PanelMovePolicy.ReachedThreshold(4,0,1)&&PanelMovePolicy.ReachedThreshold(0,-4,1),"Each axis can reach the drag threshold");
        Check(!PanelMovePolicy.ReachedThreshold(5,-5,1.5),"High DPI preserves four logical pixels of click tolerance");
        Check(PanelMovePolicy.ReachedThreshold(6,0,1.5)&&PanelMovePolicy.ReachedThreshold(0,-6,1.5),"A 150 percent display starts dragging at six physical pixels");
        Check(!PanelMovePolicy.ReachedThreshold(7,7,2)&&PanelMovePolicy.ReachedThreshold(8,-8,2),"A 200 percent display uses eight physical pixels");
        Check(!PanelMovePolicy.ReachedThreshold(4,4,1.25)&&PanelMovePolicy.ReachedThreshold(5,0,1.25),"Fractional DPI is not rounded into a shorter threshold");
        Check(PanelMovePolicy.ReachedThreshold(int.MinValue,0,1),"The negative physical delta limit does not overflow threshold checks");
        foreach(double scale in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,0d,-1d})
            Check(!PanelMovePolicy.ReachedThreshold(100,100,scale),"Malformed DPI does not begin a drag");
        var unchanged=PanelMovePolicy.Move(100,200,520,540,0,0,0,0,1920,1080);
        Check(unchanged==new PanelMovePosition(100,200),"A zero delta retains the original window position");
        var first=PanelMovePolicy.Move(100,200,520,540,30,-40,0,0,1920,1080);
        var second=PanelMovePolicy.Move(100,200,520,540,60,-80,0,0,1920,1080);
        Check(first==new PanelMovePosition(130,160)&&second==new PanelMovePosition(160,120),"Cumulative screen delta uses the initial window origin without double accumulation");
        Check(PanelMovePolicy.Move(100,200,520,540,-30,40,0,0,1920,1080)==new PanelMovePosition(70,240),"Reverse drag moves both axes without changing dimensions");
        Check(PanelMovePolicy.Move(100,200,520,540,-9999,-9999,0,0,1920,1080)==new PanelMovePosition(0,0),"Left and top positions clamp to the work area");
        Check(PanelMovePolicy.Move(100,200,520,540,9999,9999,0,0,1920,1080)==new PanelMovePosition(1400,540),"Right and bottom retain the whole panel inside the work area");
        Check(PanelMovePolicy.Move(-1400,-800,520,540,100,80,-1920,-1080,0,0)==new PanelMovePosition(-1300,-720),"Negative monitor coordinates remain physical screen coordinates");
        Check(PanelMovePolicy.Move(-1400,-500,520,540,-9999,-9999,-1920,-1080,0,0)==new PanelMovePosition(-1920,-1080),"A negative screen clamps to its own left and top edges");
        Check(PanelMovePolicy.Move(-1400,-500,520,540,9999,9999,-1920,-1080,0,0)==new PanelMovePosition(-520,-540),"A negative screen retains right and bottom visibility");
        Check(PanelMovePolicy.Move(0,0,544,634,-1400,-500,-1920,-1080,0,0)==new PanelMovePosition(-1400,-634),"A drag onto a negative monitor uses the destination work area");
        Check(PanelMovePolicy.Move(1400,540,520,540,0,0,0,0,1920,1080)==new PanelMovePosition(1400,540),"Exact edge coordinates are retained");
        Check(PanelMovePolicy.Move(100,200,2400,1200,30,40,0,0,1920,1080)==new PanelMovePosition(0,0),"An oversized panel stays anchored without inverted clamp ranges");
        Check(PanelMovePolicy.Move(-500,-400,2400,1200,30,40,-1920,-1080,0,0)==new PanelMovePosition(-1920,-1080),"An oversized panel safely anchors on a negative monitor");
        Check(PanelMovePolicy.Move(100,200,520,540,30,40,80,100,80,100)==new PanelMovePosition(80,100),"A degenerate work area has a safe anchor");
        Check(PanelMovePolicy.Move(100,200,520,540,30,40,80,100,60,50)==new PanelMovePosition(80,100),"An inverted work area never produces an inverted clamp range");
        Check(PanelMovePolicy.Move(10,10,0,-1,100,100,0,0,100,100)==new PanelMovePosition(99,99),"Malformed dimensions use a one pixel minimum when clamping");
        Check(PanelMovePolicy.Move(int.MaxValue,int.MaxValue,520,540,int.MaxValue,int.MaxValue,0,0,1920,1080)==new PanelMovePosition(1400,540),"Positive position plus delta overflow clamps in a wider numeric type");
        Check(PanelMovePolicy.Move(int.MinValue,int.MinValue,520,540,int.MinValue,int.MinValue,-1920,-1080,0,0)==new PanelMovePosition(-1920,-1080),"Negative position plus delta overflow clamps in a wider numeric type");
        Check(PanelMovePolicy.Move(int.MaxValue,int.MaxValue,1,1,0,0,int.MinValue,int.MinValue,int.MaxValue,int.MaxValue)==new PanelMovePosition(int.MaxValue-1,int.MaxValue-1),"The full integer work area avoids subtraction overflow");
        Check(PanelMovePolicy.Move(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue,0,0,int.MinValue,int.MinValue,int.MaxValue,int.MaxValue)==new PanelMovePosition(int.MinValue,int.MinValue),"An extreme size retains a valid minimum screen origin");
        Check(PanelMovePolicy.Move(0,0,int.MaxValue,int.MaxValue,0,0,int.MinValue,int.MinValue,int.MinValue+1,int.MinValue+1)==new PanelMovePosition(int.MinValue,int.MinValue),"An extreme panel on a one pixel screen cannot overflow maximum position calculation");
        return checks;
    }
}
