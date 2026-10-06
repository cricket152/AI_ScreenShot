using System.Text;

namespace ScreenShot.Core;

[Flags]
public enum MarkdownTextStyle { None=0, Bold=1, Italic=2, Strike=4, Code=8 }
public enum MarkdownTableAlignment { Left, Center, Right }
public sealed record MarkdownInline(string Text,MarkdownTextStyle Style=MarkdownTextStyle.None,Uri? Link=null);
public abstract record MarkdownBlock;
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;
public sealed record MarkdownHeading(int Level,IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;
public sealed record MarkdownCodeBlock(string Code,string Language,bool IsClosed) : MarkdownBlock;
public sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;
public sealed record MarkdownListItem(int Depth,int? Number,IReadOnlyList<MarkdownInline> Inlines);
public sealed record MarkdownList(IReadOnlyList<MarkdownListItem> Items) : MarkdownBlock;
public sealed record MarkdownTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInline>>> Rows,IReadOnlyList<MarkdownTableAlignment> Alignments) : MarkdownBlock;
public sealed record MarkdownRule : MarkdownBlock;

/// <summary>Small native-message Markdown dialect. HTML and images remain literal text.</summary>
public sealed record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks)
{
    public static MarkdownDocument Parse(string? text)
        =>new(ParseBlocks((text??"").Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n'),0));

    public static IReadOnlyList<MarkdownInline> ParseInlines(string? text)
    {
        var result=new List<MarkdownInline>();
        ParseInlineRange(text??"",MarkdownTextStyle.None,null,result,0);
        return result;
    }

    public static bool TrySafeLink(string destination,out Uri? uri)
    {
        uri=null;
        if(destination.Any(char.IsControl)||!Uri.TryCreate(destination,UriKind.Absolute,out var candidate)
            ||(candidate.Scheme!=Uri.UriSchemeHttp&&candidate.Scheme!=Uri.UriSchemeHttps)
            ||string.IsNullOrEmpty(candidate.Host)||!string.IsNullOrEmpty(candidate.UserInfo))return false;
        uri=candidate;return true;
    }

    private static IReadOnlyList<MarkdownBlock> ParseBlocks(string[] lines,int depth)
    {
        var blocks=new List<MarkdownBlock>();
        for(int index=0;index<lines.Length;)
        {
            string line=lines[index];
            if(string.IsNullOrWhiteSpace(line)){index++;continue;}
            if(TryFence(line,out char marker,out int count,out string language))
            {
                index++;var code=new List<string>();bool closed=false;
                while(index<lines.Length)
                {
                    if(IsClosingFence(lines[index],marker,count)){closed=true;index++;break;}
                    code.Add(lines[index++]);
                }
                blocks.Add(new MarkdownCodeBlock(string.Join('\n',code),language,closed));continue;
            }
            if(TryHeading(line,out int level,out string heading))
            {blocks.Add(new MarkdownHeading(level,ParseInlines(heading)));index++;continue;}
            if(IsRule(line)){blocks.Add(new MarkdownRule());index++;continue;}
            if(depth<16&&TryQuote(line,out _))
            {
                var quote=new List<string>();
                while(index<lines.Length&&TryQuote(lines[index],out string quoted)){quote.Add(quoted);index++;}
                blocks.Add(new MarkdownQuote(ParseBlocks(quote.ToArray(),depth+1)));continue;
            }
            if(TryListMarker(line,out _,out _,out _))
            {
                var items=new List<MarkdownListItem>();var indentation=new List<int>();
                while(index<lines.Length&&TryListMarker(lines[index],out int indent,out int? number,out string body))
                {
                    while(indentation.Count>0&&indentation[^1]>indent)indentation.RemoveAt(indentation.Count-1);
                    if(indentation.Count==0||indentation[^1]<indent)indentation.Add(indent);
                    int itemDepth=indentation.Count-1;index++;
                    var itemLines=new List<string>{body};
                    while(index<lines.Length&&!string.IsNullOrWhiteSpace(lines[index])
                        &&!TryListMarker(lines[index],out _,out _,out _)&&LeadingSpaces(lines[index])>indent
                        &&!IsBlockStart(lines[index]))itemLines.Add(lines[index++].TrimStart());
                    items.Add(new MarkdownListItem(itemDepth,number,ParseInlines(string.Join('\n',itemLines))));
                }
                blocks.Add(new MarkdownList(items));continue;
            }
            if(index+1<lines.Length&&TryTable(line,lines[index+1],out var headers,out var alignments))
            {
                var rows=new List<IReadOnlyList<IReadOnlyList<MarkdownInline>>>{headers.Select(ParseInlines).ToArray()};
                index+=2;
                while(index<lines.Length&&!string.IsNullOrWhiteSpace(lines[index])&&lines[index].Contains('|')&&!IsBlockStart(lines[index]))
                {
                    var cells=SplitCells(lines[index++]);
                    rows.Add(Enumerable.Range(0,headers.Count).Select(column=>ParseInlines(column<cells.Count?cells[column]:"")).ToArray());
                }
                blocks.Add(new MarkdownTable(rows,alignments));continue;
            }
            var paragraph=new List<string>{line};index++;
            while(index<lines.Length&&!string.IsNullOrWhiteSpace(lines[index])&&!IsBlockStart(lines[index])
                &&!(index+1<lines.Length&&TryTable(lines[index],lines[index+1],out _,out _)))paragraph.Add(lines[index++]);
            blocks.Add(new MarkdownParagraph(ParseInlines(string.Join('\n',paragraph))));
        }
        return blocks;
    }

    private static bool IsBlockStart(string line)=>TryFence(line,out _,out _,out _)||TryHeading(line,out _,out _)||TryQuote(line,out _)||TryListMarker(line,out _,out _,out _)||IsRule(line);
    private static int LeadingSpaces(string line)
    {
        int spaces=0;foreach(char value in line){if(value==' ')spaces++;else if(value=='\t')spaces+=4;else break;}return spaces;
    }
    private static string TrimBlockIndent(string line)=>LeadingSpaces(line)<=3?line.TrimStart():line;
    private static bool TryFence(string line,out char marker,out int count,out string language)
    {
        string trimmed=TrimBlockIndent(line);marker=trimmed.Length>0?trimmed[0]:'\0';count=0;language="";
        if(marker!='`'&&marker!='~')return false;
        while(count<trimmed.Length&&trimmed[count]==marker)count++;
        if(count<3)return false;
        string info=trimmed[count..].Trim();
        if(marker=='`'&&info.Contains('`'))return false;
        language=info.Split([' ','\t'],StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";return true;
    }
    private static bool IsClosingFence(string line,char marker,int count)
    {
        string trimmed=TrimBlockIndent(line);int run=0;
        while(run<trimmed.Length&&trimmed[run]==marker)run++;
        return run>=count&&string.IsNullOrWhiteSpace(trimmed[run..]);
    }
    private static bool TryHeading(string line,out int level,out string heading)
    {
        string trimmed=TrimBlockIndent(line);level=0;heading="";
        while(level<trimmed.Length&&trimmed[level]=='#')level++;
        if(level is <1 or >6||(level<trimmed.Length&&!char.IsWhiteSpace(trimmed[level])))return false;
        heading=trimmed[level..].Trim();int tail=heading.Length;
        while(tail>0&&heading[tail-1]=='#')tail--;
        if(tail>0&&tail<heading.Length&&char.IsWhiteSpace(heading[tail-1]))heading=heading[..tail].TrimEnd();return true;
    }
    private static bool TryQuote(string line,out string body)
    {
        string trimmed=TrimBlockIndent(line);body="";
        if(!trimmed.StartsWith('>'))return false;
        body=trimmed[1..];if(body.StartsWith(' '))body=body[1..];return true;
    }
    private static bool IsRule(string line)
    {
        string trimmed=TrimBlockIndent(line);char? marker=null;int count=0;
        foreach(char value in trimmed)
        {
            if(value==' '||value=='\t')continue;
            if(value!='-'&&value!='*'&&value!='_')return false;
            if(marker.HasValue&&marker.Value!=value)return false;
            marker=value;count++;
        }
        return count>=3;
    }
    private static bool TryListMarker(string line,out int indent,out int? number,out string body)
    {
        indent=LeadingSpaces(line);number=null;body="";string trimmed=line.TrimStart();int end=0;
        if(trimmed.Length>=2&&(trimmed[0]=='-'||trimmed[0]=='+'||trimmed[0]=='*')&&char.IsWhiteSpace(trimmed[1]))end=1;
        else
        {
            while(end<trimmed.Length&&char.IsAsciiDigit(trimmed[end]))end++;
            if(end is <1 or >9||end+1>=trimmed.Length||(trimmed[end]!='.'&&trimmed[end]!=')')
                ||!char.IsWhiteSpace(trimmed[end+1])||!int.TryParse(trimmed[..end],out int value))return false;
            number=value;end++;
        }
        body=trimmed[(end+1)..].TrimStart();return true;
    }
    private static bool TryTable(string header,string delimiter,out IReadOnlyList<string> headers,out IReadOnlyList<MarkdownTableAlignment> alignments)
    {
        headers=[];alignments=[];if(!header.Contains('|'))return false;
        var cells=SplitCells(delimiter);var headerCells=SplitCells(header);var alignment=new List<MarkdownTableAlignment>();
        if(cells.Count!=headerCells.Count||cells.Count==0)return false;
        foreach(string cell in cells)
        {
            string value=cell.Trim();bool left=value.StartsWith(':'),right=value.EndsWith(':');
            string dashes=value.Trim(':');if(dashes.Length<3||dashes.Any(character=>character!='-'))return false;
            alignment.Add(left&&right?MarkdownTableAlignment.Center:right?MarkdownTableAlignment.Right:MarkdownTableAlignment.Left);
        }
        headers=headerCells;alignments=alignment;return true;
    }
    private static IReadOnlyList<string> SplitCells(string line)
    {
        string value=line.Trim();if(value.StartsWith('|'))value=value[1..];
        if(value.EndsWith('|')&&!IsEscaped(value,value.Length-1))value=value[..^1];
        var cells=new List<string>();int start=0,codeRun=0;
        for(int index=0;index<value.Length;index++)
        {
            if(IsEscaped(value,index))continue;
            if(value[index]=='`')
            {
                int run=1;while(index+run<value.Length&&value[index+run]=='`')run++;
                if(codeRun==0)codeRun=run;else if(codeRun==run)codeRun=0;
                index+=run-1;
            }
            else if(value[index]=='|'&&codeRun==0){cells.Add(value[start..index].Trim());start=index+1;}
        }
        cells.Add(value[start..].Trim());return cells;
    }

    private static void ParseInlineRange(string text,MarkdownTextStyle style,Uri? link,List<MarkdownInline> output,int depth)
    {
        if(depth>=16){Append(output,text,style,link);return;}
        var literal=new StringBuilder();
        var unclosedDelimiters=new HashSet<string>(StringComparer.Ordinal);
        int lastLinkClose=text.LastIndexOf("](",StringComparison.Ordinal);
        void Flush(){Append(output,literal.ToString(),style,link);literal.Clear();}
        for(int index=0;index<text.Length;)
        {
            char value=text[index];
            if(value=='\\'&&index+1<text.Length&&"\\`*_{}[]()#+-.!|>~".Contains(text[index+1]))
            {literal.Append(text[index+1]);index+=2;continue;}
            if(value=='`')
            {
                int count=1;while(index+count<text.Length&&text[index+count]=='`')count++;
                int end=FindDelimiter(text,index+count,new string('`',count),exactRun:true);
                if(end>=0)
                {
                    Flush();string code=text[(index+count)..end].Replace('\n',' ');
                    if(code.Length>1&&code.StartsWith(' ')&&code.EndsWith(' ')&&code.Any(character=>character!=' '))code=code[1..^1];
                    Append(output,code,style|MarkdownTextStyle.Code,link);index=end+count;continue;
                }
                literal.Append(text,index,count);index+=count;continue;
            }
            if(index<lastLinkClose&&(value=='['||(value=='!'&&index+1<text.Length&&text[index+1]=='['))&&TryLink(text,index,out int consumed,out string label,out string destination))
            {
                if(value!='!'&&TrySafeLink(destination,out var safe))
                {Flush();ParseInlineRange(label,style,link??safe,output,depth+1);}
                else literal.Append(text,index,consumed);
                index+=consumed;continue;
            }
            if(value=='<'&&text.IndexOf('>',index+1) is int autoEnd&&autoEnd>index&&TrySafeLink(text[(index+1)..autoEnd],out var autoLink))
            {Flush();Append(output,text[(index+1)..autoEnd],style,link??autoLink);index=autoEnd+1;continue;}
            if(value=='*'||value=='_'||value=='~')
            {
                int run=1;while(index+run<text.Length&&text[index+run]==value)run++;
                int count=value=='~'?2:Math.Min(run,3);
                bool boundary=value!='_'||index==0||!char.IsLetterOrDigit(text[index-1]);
                if((value!='~'||run>=2)&&boundary&&index+count<text.Length&&!char.IsWhiteSpace(text[index+count]))
                {
                    string delimiter=new(value,count);int end=unclosedDelimiters.Contains(delimiter)?-1:FindEmphasisEnd(text,index+count,delimiter);
                    if(end>=0)
                    {
                        Flush();MarkdownTextStyle added=value=='~'?MarkdownTextStyle.Strike:count==3?MarkdownTextStyle.Bold|MarkdownTextStyle.Italic:count==2?MarkdownTextStyle.Bold:MarkdownTextStyle.Italic;
                        ParseInlineRange(text[(index+count)..end],style|added,link,output,depth+1);index=end+count;continue;
                    }
                    unclosedDelimiters.Add(delimiter);
                }
                literal.Append(text,index,run);index+=run;continue;
            }
            literal.Append(value);index++;
        }
        Flush();
    }
    private static bool TryLink(string text,int index,out int consumed,out string label,out string destination)
    {
        consumed=0;label="";destination="";int open=text[index]=='!'?index+1:index,level=1,close=-1;
        for(int cursor=open+1;cursor<text.Length;cursor++)
        {
            if(IsEscaped(text,cursor))continue;
            if(text[cursor]=='[')level++;
            if(text[cursor]==']'&&--level==0){close=cursor;break;}
        }
        if(close<0||close+1>=text.Length||text[close+1]!='(')return false;
        int parentheses=1,end=-1;
        for(int cursor=close+2;cursor<text.Length;cursor++)
        {
            if(IsEscaped(text,cursor))continue;
            if(text[cursor]=='(')parentheses++;
            if(text[cursor]==')'&&--parentheses==0){end=cursor;break;}
        }
        if(end<0)return false;
        label=text[(open+1)..close];destination=text[(close+2)..end].Trim();
        if(destination.StartsWith('<')&&destination.EndsWith('>'))destination=destination[1..^1];
        consumed=end-index+1;return true;
    }
    private static int FindEmphasisEnd(string text,int start,string delimiter)
    {
        for(int index=start;index<text.Length;index++)
        {
            if(IsEscaped(text,index))continue;
            if(text[index]=='`')
            {
                int codeRun=1;while(index+codeRun<text.Length&&text[index+codeRun]=='`')codeRun++;
                int close=FindDelimiter(text,index+codeRun,new string('`',codeRun),exactRun:true);
                if(close>=0)index=close+codeRun-1;
                continue;
            }
            if(text[index]!=delimiter[0])continue;
            int run=1;while(index+run<text.Length&&text[index+run]==delimiter[0])run++;
            if(run<delimiter.Length||(delimiter.Length==1&&run==2)){index+=run-1;continue;}
            int candidate=index+run-delimiter.Length;
            bool before=index>start&&!char.IsWhiteSpace(text[index-1]);int after=index+run;
            bool boundary=delimiter[0]!='_'||after==text.Length||!char.IsLetterOrDigit(text[after]);
            if(before&&boundary)return candidate;
            index+=run-1;
        }
        return -1;
    }
    private static int FindDelimiter(string text,int start,string delimiter,bool exactRun)
    {
        int index=start;
        while((index=text.IndexOf(delimiter,index,StringComparison.Ordinal))>=0)
        {
            if(!IsEscaped(text,index)&&(!exactRun||((index==0||text[index-1]!=delimiter[0])
                &&(index+delimiter.Length==text.Length||text[index+delimiter.Length]!=delimiter[0]))))return index;
            index+=delimiter.Length;
        }
        return -1;
    }
    private static bool IsEscaped(string text,int index)
    {
        int count=0;while(index>0&&text[--index]=='\\')count++;return count%2!=0;
    }
    private static void Append(List<MarkdownInline> output,string text,MarkdownTextStyle style,Uri? link)
    {
        if(text.Length==0)return;
        if(output.Count>0&&output[^1].Style==style&&output[^1].Link==link)output[^1]=output[^1] with {Text=output[^1].Text+text};
        else output.Add(new MarkdownInline(text,style,link));
    }
}
