using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ScreenShot.Core;

namespace ScreenShot.App;

/// <summary>Selectable native Markdown for user and provider message bubbles.</summary>
internal sealed class MarkdownMessageView : UserControl
{
    private readonly StackPanel surface=new(){Spacing=UiDesign.Number("SS.Spacing.S"),HorizontalAlignment=HorizontalAlignment.Stretch};
    internal RichTextBlock SelectionSurface {get;}=new(){TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,HorizontalAlignment=HorizontalAlignment.Stretch};
    internal Button? CopyButton {get;}
    internal Task? CopyTask {get;private set;}
    private readonly TextBlock copyFeedback=new(){VerticalAlignment=VerticalAlignment.Center};
    private readonly DispatcherQueueTimer renderTimer;
    private string text="";
    private bool renderingPending,suspended;
    private Action? afterRender;
    internal MarkdownDocument Document {get;private set;}=MarkdownDocument.Parse("");
    internal int RenderedBlockCount {get;private set;}
    internal int RenderedControlCount {get;private set;}
    public string Text
    {
        get=>text;
        set
        {
            string next=value??"";
            renderTimer.Stop();afterRender=null;
            if(next==text&&!renderingPending)return;
            text=next;renderingPending=true;FlushRendering();
        }
    }

    internal MarkdownMessageView(Func<string,Task>? copyText=null)
    {
        Content=surface;HorizontalContentAlignment=HorizontalAlignment.Stretch;
        UiDesign.Apply(SelectionSurface,"SS.RichTextStyle");surface.Children.Add(SelectionSurface);
        if(copyText!=null)
        {
            CopyButton=new Button{Content=UiDesign.Icon("\uE8C8",14),HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=false};
            UiDesign.Apply(CopyButton,"SS.MessageCopyButtonStyle");UiDesign.Label(CopyButton,"复制这条回答的完整内容");ToolTipService.SetToolTip(CopyButton,"复制全文");
            UiDesign.Apply(copyFeedback,"SS.CaptionTextStyle");
            var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=UiDesign.Number("SS.Spacing.S")};
            actions.Children.Add(CopyButton);actions.Children.Add(copyFeedback);surface.Children.Add(actions);
            CopyButton.Click+=(_,_)=>CopyTask=CopyAsync(copyText);
        }
        renderTimer=DispatcherQueue.CreateTimer();renderTimer.Interval=TimeSpan.FromMilliseconds(80);renderTimer.IsRepeating=false;
        renderTimer.Tick+=(_,_)=>FlushRendering();
        Unloaded+=(_,_)=>{suspended=true;renderTimer.Stop();afterRender=null;};
        Loaded+=(_,_)=>{suspended=false;FlushRendering();};
    }

    private async Task CopyAsync(Func<string,Task> copyText)
    {
        if(CopyButton==null||!CopyButton.IsEnabled)return;
        string snapshot=text;CopyButton.IsEnabled=false;copyFeedback.Text="正在复制…";
        try{await copyText(snapshot);copyFeedback.Text="已复制";}
        catch(OperationCanceledException){copyFeedback.Text="复制已取消";}
        catch(Exception){copyFeedback.Text="复制失败，请重试";}
        finally{CopyButton.IsEnabled=!string.IsNullOrWhiteSpace(text);}
    }

    internal void SetStreamingText(string? value,Action? afterRender=null)
    {
        string next=value??"";
        if(next!=text){text=next;renderingPending=true;}
        this.afterRender=suspended?null:afterRender;
        if(!renderingPending)
        {
            var callback=this.afterRender;this.afterRender=null;callback?.Invoke();return;
        }
        // Keep the first deadline while newer snapshots replace the pending text.
        if(!suspended&&!renderTimer.IsRunning)renderTimer.Start();
    }

    internal void FlushRendering()
    {
        renderTimer.Stop();if(!renderingPending)return;
        var callback=afterRender;afterRender=null;
        Document=MarkdownDocument.Parse(text);SelectionSurface.Blocks.Clear();RenderedBlockCount=0;RenderedControlCount=1;
        // Every visible character belongs to one document, including code and table cells.
        // Child text controls inside InlineUIContainer would split selection again.
        AddBlocks(Document.Blocks);renderingPending=false;
        if(CopyButton!=null&&CopyTask?.IsCompleted!=false){CopyButton.IsEnabled=!string.IsNullOrWhiteSpace(text);copyFeedback.Text="";}
        if(!suspended)callback?.Invoke();
    }

    private void AddBlocks(IReadOnlyList<MarkdownBlock> blocks,double indent=0)
    {
        foreach(var block in blocks)
        {
            RenderedBlockCount++;
            switch(block)
            {
                case MarkdownParagraph paragraph:AddParagraph(paragraph.Inlines,indent);break;
                case MarkdownHeading heading:
                    var title=AddParagraph(heading.Inlines,indent);title.FontSize=heading.Level==1?UiDesign.Number("SS.Typography.Section"):UiDesign.Number("SS.Typography.Body");title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;break;
                case MarkdownCodeBlock code:
                    if(code.Language.Length>0){var language=AddParagraph([new(code.Language)],indent);language.FontSize=UiDesign.Number("SS.Typography.Caption");}
                    var codeParagraph=AddParagraph([new(code.Code)],indent);codeParagraph.FontFamily=new FontFamily("Consolas");codeParagraph.FontSize=UiDesign.Number("SS.Typography.Caption");break;
                case MarkdownQuote quote:
                    AddBlocks(quote.Blocks,indent+UiDesign.Number("SS.Spacing.L"));break;
                case MarkdownList list:
                    foreach(var item in list.Items)
                    {
                        var row=AddParagraph([],indent+Math.Min(item.Depth,12)*UiDesign.Number("SS.Spacing.L"));
                        row.Inlines.Add(new Run{Text=item.Number.HasValue?item.Number.Value+". ":"• "});AddInlines(row,item.Inlines);
                    }
                    break;
                case MarkdownTable table:
                    for(int row=0;row<table.Rows.Count;row++)
                    {
                        var tableRow=AddParagraph([],indent);tableRow.FontFamily=new FontFamily("Consolas");tableRow.FontSize=UiDesign.Number("SS.Typography.Caption");
                        if(row==0)tableRow.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
                        for(int column=0;column<table.Alignments.Count;column++)
                        {
                            if(column>0)tableRow.Inlines.Add(new Run{Text=" │ "});
                            AddInlines(tableRow,table.Rows[row][column]);
                        }
                    }
                    break;
                case MarkdownRule:
                    AddParagraph([new("────────")],indent);break;
            }
        }
    }

    private Paragraph AddParagraph(IReadOnlyList<MarkdownInline> inlines,double indent)
    {
        var paragraph=new Paragraph{Margin=new Thickness(indent,0,0,UiDesign.Number("SS.Spacing.S"))};
        AddInlines(paragraph,inlines);SelectionSurface.Blocks.Add(paragraph);return paragraph;
    }

    private static void AddInlines(Paragraph paragraph,IReadOnlyList<MarkdownInline> inlines)
    {
        foreach(var part in inlines)
        {
            var run=new Run{Text=part.Text};
            if(part.Style.HasFlag(MarkdownTextStyle.Bold))run.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            if(part.Style.HasFlag(MarkdownTextStyle.Italic))run.FontStyle=Windows.UI.Text.FontStyle.Italic;
            if(part.Style.HasFlag(MarkdownTextStyle.Strike))run.TextDecorations=Windows.UI.Text.TextDecorations.Strikethrough;
            if(part.Style.HasFlag(MarkdownTextStyle.Code)){run.FontFamily=new FontFamily("Consolas");run.FontSize=UiDesign.Number("SS.Typography.Caption");}
            if(part.Link!=null&&MarkdownDocument.TrySafeLink(part.Link.OriginalString,out var uri))
            {
                var hyperlink=new Hyperlink{NavigateUri=uri};hyperlink.Inlines.Add(run);paragraph.Inlines.Add(hyperlink);
            }
            else paragraph.Inlines.Add(run);
        }
    }
}
