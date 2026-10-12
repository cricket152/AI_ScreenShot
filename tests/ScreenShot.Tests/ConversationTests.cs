using ScreenShot.Core;

internal static class ConversationTests
{
    internal static int Run()
    {
        int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        string root=Path.Combine(Path.GetTempPath(),"AIScreenShot-conversation-test-"+Guid.NewGuid().ToString("N"));
        var first=new Conversation{Title="隔离会话",Workspace=new("one",root),Client="Agent",Order=0};
        var second=new Conversation{Title="另一个会话",Workspace=new("two",root+"-two"),Order=1,ThreadId="native-thread",TurnId="native-turn"};
        try
        {
            string image;
            using(var store=new ConversationStore(root))
            {
                Check(File.Exists(Path.Combine(root,"conversations.db")),"SQLite initializes without a service or manual schema setup");
                image=store.SaveImage([1,2,3,4]);first.DraftImage=image;
                first.Messages.Add(new(){Role="user",Text="generated prompt",Image=image,Client="Agent",State="Sent"});
                first.Messages.Add(new(){Role="assistant",Text="generated reply",Client="Agent",Model="test",State="Sent"});
                second.Messages.Add(new(){Role="assistant",Text="partial output",State="Thinking"});
                store.Save(first);store.Save(second);store.Select(second.Id);
                first.Title="手动标题";first.ManualTitle=true;first.IsOpen=false;store.Save(first);
            }
            using(var store=new ConversationStore(root))
            {
                var chats=store.Load();
                Check(chats.Count==2&&store.SelectedId==second.Id,"Restart restores tab order and selected tab");
                Check(!chats[0].IsOpen&&chats[0].Title=="手动标题"&&chats[0].ManualTitle,"Closing the view preserves history and manual title");
                Check(chats[0].Workspace?.Name=="one"&&chats[1].Workspace?.Name=="two"&&chats[1].ThreadId=="native-thread"&&chats[1].TurnId=="native-turn","Workspace and native session IDs are independent and durable");
                Check(chats[0].Messages.Count==2&&chats[0].Messages[1].Model=="test"&&chats[1].Messages.Single().State=="Unknown","Messages survive restart and unfinished sends become Unknown");
                Check(store.ReadImage(image).SequenceEqual(new byte[]{1,2,3,4}),"Screenshots survive restart outside SQLite in local storage");
                Check(store.ReadImage("../settings.json").Length==0&&store.ReadImage("..\\settings.json").Length==0,"Stored image paths cannot escape the screenshot directory");
                var broken=new Conversation{Title=null!};store.Save(broken);
                Check(store.Load().Count==2&&store.Warning!=null,"A malformed row is skipped without deleting it or blocking valid conversations");
                var badMessage=new Conversation{Images=null!,Messages=[new(){Id=null!}]};store.Save(badMessage);
                Check(store.Load().Count==2,"Malformed message identifiers cannot break native view restoration");
                store.Delete(first);Check(store.Load().Count==1&&store.ReadImage(image).Length==0,"Permanent deletion removes only the selected history and its screenshot files");
                Check(store.Load()[0].ThreadId=="native-thread","Deleting one chat preserves another native session");
            }
            File.WriteAllText(Path.Combine(root,"conversations.db"),"generated corrupt database");
            using(var store=new ConversationStore(root))
            {Check(store.Load().Count==0&&store.Warning!=null&&Directory.GetFiles(root,"*.damaged-*").Length>0,"Database corruption preserves a recovery backup and initializes a usable database");}
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
}
