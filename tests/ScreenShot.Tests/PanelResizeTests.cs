using ScreenShot.App;

internal static class PanelResizeTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        var limits=new PanelResizeLimits(320,900,56,720);
        Check(PanelResizePolicy.HitTest(0,0,520,540)==(PanelResizeEdges.Left|PanelResizeEdges.Top),"Panel corner accepts both resize axes");
        Check(PanelResizePolicy.HitTest(520,540,520,540)==(PanelResizeEdges.Right|PanelResizeEdges.Bottom),"Panel bottom right corner accepts both resize axes");
        Check(PanelResizePolicy.HitTest(260,0,520,540)==PanelResizeEdges.Top,"Panel top edge accepts vertical resize");
        Check(PanelResizePolicy.HitTest(0,270,520,540)==PanelResizeEdges.Left,"Panel left edge accepts horizontal resize");
        Check(PanelResizePolicy.HitTest(260,270,520,540)==PanelResizeEdges.None,"Panel contents cannot begin an edge resize");
        Check(PanelResizePolicy.HitTest(-1,1,520,540)==PanelResizeEdges.None&&PanelResizePolicy.HitTest(521,1,520,540)==PanelResizeEdges.None,"Outside panel cannot begin resize");
        Check(PanelResizePolicy.HitTest(double.NaN,0,520,540)==PanelResizeEdges.None,"Malformed pointer cannot begin resize");
        Check(PanelResizePolicy.HitTest(1,3,4,4)==(PanelResizeEdges.Left|PanelResizeEdges.Bottom),"Tiny panel chooses nearest edge without contradictory flags");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Window,PanelResizeEdges.Top,true)==PanelResizePane.Message,"Window top resize changes the visible message height");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Window,PanelResizeEdges.Top,false)==PanelResizePane.Toolbar,"Window top resize changes toolbar height when the message pane is collapsed");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Window,PanelResizeEdges.Bottom,true)==PanelResizePane.Message,"Window bottom resize selects the message pane while it is visible");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Window,PanelResizeEdges.Bottom,false)==PanelResizePane.Toolbar,"Window bottom resize selects the toolbar when the message pane is collapsed");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Top,true)==PanelResizePane.Message,"Toolbar top resizes the full panel through message height rather than shrinking toolbar controls");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Top,false)==PanelResizePane.Toolbar,"Toolbar top retains toolbar sizing when it is the only visible pane");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Left|PanelResizeEdges.Top,true)==PanelResizePane.Message,"Toolbar top left corner retains window top resize semantics");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Right|PanelResizeEdges.Top,true)==PanelResizePane.Message,"Toolbar top right corner retains window top resize semantics");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Bottom,true)==PanelResizePane.Toolbar,"Toolbar bottom resize keeps its independent height");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Left,true)==PanelResizePane.Toolbar,"Toolbar horizontal resize keeps its original pane");
        Check(PanelResizePolicy.ResolvePane(PanelResizePane.Toolbar,PanelResizeEdges.Right|PanelResizeEdges.Bottom,true)==PanelResizePane.Toolbar,"Toolbar bottom corner retains independent toolbar height");
        foreach(bool messageVisible in new[]{false,true})
            foreach(var edges in new[]{PanelResizeEdges.Top,PanelResizeEdges.Bottom,PanelResizeEdges.Left,PanelResizeEdges.Right,PanelResizeEdges.Left|PanelResizeEdges.Top,PanelResizeEdges.Right|PanelResizeEdges.Bottom})
                Check(PanelResizePolicy.ResolvePane(PanelResizePane.Message,edges,messageVisible)==PanelResizePane.Message,"Message resize does not change the selected pane");
        var right=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right,100,250,limits);
        Check(right==new PanelResizeChange(620,540,0,0),"Right resize changes shared width without changing pane height or window origin");
        var left=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Left,100,0,limits);
        Check(left==new PanelResizeChange(420,540,100,0)&&left.Width+left.LeftDelta==520,"Left resize retains opposite physical anchor");
        var top=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Top,0,-100,limits);
        Check(top==new PanelResizeChange(520,640,0,-100)&&top.Height+top.TopDelta==540,"Top resize retains opposite vertical anchor");
        var fullPanelTop=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Top,0,24,new(420,900,320,720));
        const double fixedToolbarAndMargins=56+12+26;
        Check(fullPanelTop.Height==516&&fullPanelTop.TopDelta==24,"Dragging a full panel top down reduces message height even when toolbar is at its minimum height");
        Check(fullPanelTop.Height+fixedToolbarAndMargins+fullPanelTop.TopDelta==540+fixedToolbarAndMargins,"Full panel top resize keeps its overall bottom anchor while preserving toolbar height and margins");
        var fullPanelRestore=PanelResizePolicy.Apply(520,fullPanelTop.Height,PanelResizeEdges.Top,0,-24,new(420,900,320,720));
        Check(fullPanelRestore.Height==540&&fullPanelTop.TopDelta+fullPanelRestore.TopDelta==0,"Reverse top drag restores message height and window origin");
        var bottom=PanelResizePolicy.Apply(520,56,PanelResizeEdges.Bottom,0,24,limits);
        Check(bottom==new PanelResizeChange(520,80,0,0),"Toolbar height can grow independently of shared width");
        var corner=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Left|PanelResizeEdges.Top,500,700,limits);
        Check(corner==new PanelResizeChange(320,56,200,484),"Min size clamp updates origin by actual size change, not requested pointer delta");
        var maximum=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right|PanelResizeEdges.Bottom,9999,9999,limits);
        Check(maximum==new PanelResizeChange(900,720,0,0),"Maximum pane size obeys monitor supplied limits");
        var reverse=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Left|PanelResizeEdges.Top,-9999,-9999,limits);
        Check(reverse==new PanelResizeChange(900,720,-380,-180),"Left and top growth also obeys size limits while retaining opposite anchors");
        var dpi=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right,150/1.5,0,limits);
        Check(dpi.Width==620,"Physical pointer delta is converted to logical units exactly once");
        Check(PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right,0,0,limits)==new PanelResizeChange(520,540,0,0),"Start position remains unchanged before movement");
        var partial=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right,40,0,limits);var final=PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right,80,0,limits);
        Check(partial.Width==560&&final.Width==600,"Repeated cumulative updates use drag snapshot rather than accumulating twice");
        Check(PanelResizePolicy.Apply(520,540,PanelResizeEdges.None,100,100,limits)==new PanelResizeChange(520,540,0,0),"No selected edges cannot change dimensions");
        try{PanelResizePolicy.Apply(520,540,PanelResizeEdges.Left|PanelResizeEdges.Right,1,1,limits);Check(false,"Contradictory edges are invalid");}catch(ArgumentOutOfRangeException){Check(true,"Contradictory edges are rejected");}
        try{PanelResizePolicy.Apply(520,540,PanelResizeEdges.Right,double.NaN,0,limits);Check(false,"Malformed delta is invalid");}catch(ArgumentOutOfRangeException){Check(true,"Malformed delta is rejected");}
        try{PanelResizePolicy.Apply(520,540,PanelResizeEdges.Bottom,0,1,new(320,900,100,56));Check(false,"Inverted limits are invalid");}catch(ArgumentOutOfRangeException){Check(true,"Inverted size limits are rejected");}
        return checks;
    }
}
