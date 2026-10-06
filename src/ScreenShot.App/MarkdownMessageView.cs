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

    internal MarkdownMessageView()
    {
        Content=surface;HorizontalContentAlignment=HorizontalAlignment.Stretch;
        renderTimer=DispatcherQueue.CreateTimer();renderTimer.Interval=TimeSpan.FromMilliseconds(80);renderTimer.IsRepeating=false;
        renderTimer.Tick+=(_,_)=>FlushRendering();
        Unloaded+=(_,_)=>{suspended=true;renderTimer.Stop();afterRender=null;};
        Loaded+=(_,_)=>{suspended=false;FlushRendering();};
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
        Document=MarkdownDocument.Parse(text);surface.Children.Clear();RenderedBlockCount=0;RenderedControlCount=0;
        AddBlocks(surface,Document.Blocks);renderingPending=false;
        if(!suspended)callback?.Invoke();
    }

    private void AddBlocks(StackPanel parent,IReadOnlyList<MarkdownBlock> blocks)
    {
        foreach(var block in blocks)
        {
            RenderedBlockCount++;
            switch(block)
            {
                case MarkdownParagraph paragraph:parent.Children.Add(RichText(paragraph.Inlines));break;
                case MarkdownHeading heading:
                    parent.Children.Add(RichText(heading.Inlines,heading.Level==1?UiDesign.Number("SS.Typography.Section"):UiDesign.Number("SS.Typography.Body"),true));break;
                case MarkdownCodeBlock code:parent.Children.Add(Code(code));break;
                case MarkdownQuote quote:
                    var quoted=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.S")};AddBlocks(quoted,quote.Blocks);
                    var quoteBorder=ThemeBorder("CardBackgroundFillColorDefaultBrush");quoteBorder.BorderThickness=new Thickness(2,0,0,0);quoteBorder.Padding=new Thickness(UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.XS"),UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.XS"));quoteBorder.Child=quoted;
                    parent.Children.Add(quoteBorder);RenderedControlCount+=2;break;
                case MarkdownList list:
                    var items=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.XS")};
                    foreach(var item in list.Items)
                    {
                        var row=new Grid{ColumnSpacing=UiDesign.Number("SS.Spacing.S"),Margin=new Thickness(Math.Min(item.Depth,12)*UiDesign.Number("SS.Spacing.L"),0,0,0)};
                        row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                        var marker=new TextBlock{Text=item.Number.HasValue?item.Number.Value+".":"•",MinWidth=UiDesign.Number("SS.Spacing.L"),VerticalAlignment=VerticalAlignment.Top};UiDesign.Apply(marker,"SS.BodyTextStyle");row.Children.Add(marker);
                        var body=RichText(item.Inlines);Grid.SetColumn(body,1);row.Children.Add(body);items.Children.Add(row);RenderedControlCount+=2;
                    }
                    parent.Children.Add(items);RenderedControlCount++;break;
                case MarkdownTable table:parent.Children.Add(Table(table));break;
                case MarkdownRule:
                    var rule=ThemeBorder("DividerStrokeColorDefaultBrush");rule.Height=1;parent.Children.Add(rule);RenderedControlCount++;break;
            }
        }
    }

    private RichTextBlock RichText(IReadOnlyList<MarkdownInline> inlines,double? size=null,bool bold=false)
    {
        var control=new RichTextBlock{TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,HorizontalAlignment=HorizontalAlignment.Stretch};UiDesign.Apply(control,"SS.RichTextStyle");
        if(size.HasValue)control.FontSize=size.Value;
        if(bold)control.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var paragraph=new Paragraph{Margin=new Thickness(0)};control.Blocks.Add(paragraph);
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
        RenderedControlCount++;return control;
    }

    private FrameworkElement Code(MarkdownCodeBlock code)
    {
        var body=new TextBlock{Text=code.Code,IsTextSelectionEnabled=true,Margin=UiDesign.Thickness("SS.Spacing.S")};UiDesign.Apply(body,"SS.BodyTextStyle");body.FontFamily=new FontFamily("Consolas");body.FontSize=UiDesign.Number("SS.Typography.Caption");body.TextWrapping=TextWrapping.NoWrap;
        var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled};
        var content=new StackPanel{Spacing=UiDesign.Number("SS.Spacing.XS")};
        if(code.Language.Length>0){var language=new TextBlock{Text=code.Language,Margin=new Thickness(UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.S"),0)};UiDesign.Apply(language,"SS.CaptionTextStyle");content.Children.Add(language);RenderedControlCount++;}
        content.Children.Add(scroll);
        var border=ThemeBorder("CardBackgroundFillColorSecondaryBrush");border.CornerRadius=UiDesign.Corner("SS.Corner.Control");border.Child=content;RenderedControlCount+=4;return border;
    }

    private FrameworkElement Table(MarkdownTable table)
    {
        var grid=new Grid();
        foreach(var _ in table.Alignments)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        for(int row=0;row<table.Rows.Count;row++)
        {
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            for(int column=0;column<table.Alignments.Count;column++)
            {
                var body=RichText(table.Rows[row][column],UiDesign.Number("SS.Typography.Caption"),row==0);body.MinWidth=64;body.MaxWidth=320;
                body.TextAlignment=table.Alignments[column] switch {MarkdownTableAlignment.Center=>TextAlignment.Center,MarkdownTableAlignment.Right=>TextAlignment.Right,_=>TextAlignment.Left};
                var cell=ThemeBorder(row==0?"CardBackgroundFillColorDefaultBrush":"CardBackgroundFillColorSecondaryBrush");cell.BorderThickness=new Thickness(0,0,1,1);cell.Padding=new Thickness(UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.XS"),UiDesign.Number("SS.Spacing.S"),UiDesign.Number("SS.Spacing.XS"));cell.Child=body;
                Grid.SetRow(cell,row);Grid.SetColumn(cell,column);grid.Children.Add(cell);RenderedControlCount++;
            }
        }
        var scroll=new ScrollViewer{Content=grid,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled};
        RenderedControlCount+=2;return scroll;
    }

    private static Border ThemeBorder(string background)
        =>(Border)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{{ThemeResource {background}}}\" BorderBrush=\"{{ThemeResource DividerStrokeColorDefaultBrush}}\" />");
}
