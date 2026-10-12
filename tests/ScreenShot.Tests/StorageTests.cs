using ScreenShot.Core;
using System.Runtime.InteropServices;

internal static class StorageTests
{
    internal static int Run()
    {
        int checks=0;void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
        string root=Path.Combine(Path.GetTempPath(),"AIScreenShot-storage-test-"+Guid.NewGuid().ToString("N"));
        string folder=ScreenshotStorage.Folder(root);
        try
        {
            var empty=ScreenshotStorage.Prepare(root);
            Check(empty.Info.FileCount==0&&empty.Info.UnusedCount==0&&!Directory.Exists(root),"Empty storage inspection must not create a user directory or database");
            using var store=new ConversationStore(root);
            var chat=new Conversation{IsOpen=false};store.SaveDraftImage(chat,[1,2,3]);string draft=chat.DraftImage!;
            string history=store.SaveImage([4,5,6,7]);chat.Messages.Add(new(){Role="user",State="Thinking",Image=history});store.Save(chat);
            string obsolete=store.SaveImage([8,9]);chat.Images.Add(obsolete);store.Save(chat);
            string orphan=store.SaveImage([10]);
            File.WriteAllBytes(Path.Combine(folder,"user-image.png"),[11,12,13,14,15]);
            File.WriteAllText(Path.Combine(folder,"keep.txt"),"generated unrelated fixture");
            Directory.CreateDirectory(Path.Combine(folder,"nested"));string nested=Path.Combine(folder,"nested",Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(nested,[16]);
            var plan=ScreenshotStorage.Prepare(root);
            Check(plan.Info.FileCount==5&&plan.Info.TotalBytes==15&&plan.Info.UnusedCount==2&&plan.Info.UnusedBytes==3,"Storage totals or live reference classification are incorrect");
            Check(store.Load().Single().IsOpen==false&&plan.Info.CanClean,"Closed history and active request images remain protected");
            var cleaned=ScreenshotStorage.Clean(plan);
            Check(cleaned==new ScreenshotCleanupResult(2,3,0,0),"Cleanup result must report actual deleted count and bytes");
            Check(store.ReadImage(draft).Length==3&&store.ReadImage(history).Length==4&&store.ReadImage(obsolete).Length==0&&store.ReadImage(orphan).Length==0,"Cleanup must preserve drafts/streaming history and remove obsolete ownership-only files");
            Check(File.Exists(Path.Combine(folder,"user-image.png"))&&File.Exists(Path.Combine(folder,"keep.txt"))&&File.Exists(nested),"Cleanup must leave unknown filenames, other file types and nested directories intact");
            Check(ScreenshotStorage.Prepare(root).Info.UnusedCount==0,"Second scan must reflect reclaimed storage");
            string late=store.SaveImage([17,18]);var beforeReference=ScreenshotStorage.Prepare(root);
            using(var other=new ConversationStore(root)){chat.Messages.Add(new(){Image=late,State="Sent"});other.Save(chat);}
            Check(ScreenshotStorage.Clean(beforeReference).SkippedCount==1&&store.ReadImage(late).Length==2,"A new reference from another store after preview must protect the candidate");
            string changed=store.SaveImage([19]);var beforeChange=ScreenshotStorage.Prepare(root);File.WriteAllBytes(Path.Combine(folder,changed),[19,20]);
            Check(ScreenshotStorage.Clean(beforeChange).SkippedCount==1&&store.ReadImage(changed).Length==2,"A changed file must be skipped instead of deleting a replacement");
            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();bool stopped=false;try{ScreenshotStorage.Clean(ScreenshotStorage.Prepare(root),canceled.Token);}catch(OperationCanceledException){stopped=true;}
                Check(stopped&&store.ReadImage(changed).Length==2,"Canceled cleanup must not delete any candidate");
            }
            using(var locked=new FileStream(Path.Combine(folder,changed),FileMode.Open,FileAccess.Read,FileShare.Read))
                Check(ScreenshotStorage.Clean(ScreenshotStorage.Prepare(root)).FailedCount==1,"Locked files must remain intact and count as failed cleanup");
            ScreenshotStorage.Clean(ScreenshotStorage.Prepare(root));
            var writer=Task.Run(()=>{for(int index=0;index<30;index++)store.SaveDraftImage(chat,[(byte)index,21]);});
            for(int index=0;index<5;index++)ScreenshotStorage.Clean(ScreenshotStorage.Prepare(root));writer.GetAwaiter().GetResult();
            Check(store.ReadImage(chat.DraftImage).Length==2&&store.ReadImage(history).Length==4,"Concurrent capture persistence and cleanup must retain committed current/history images");
            string protectedByBadIndex=store.SaveImage([22]);var validPlan=ScreenshotStorage.Prepare(root);
            store.Save(new Conversation{Title=null!});
            Check(!ScreenshotStorage.Prepare(root).Info.CanClean,"Malformed history must disable cleanup instead of assuming skipped rows have no references");
            bool refused=false;try{ScreenshotStorage.Clean(validPlan);}catch(InvalidDataException){refused=true;}
            Check(refused&&store.ReadImage(protectedByBadIndex).Length==1,"A malformed index after confirmation must stop cleanup before deleting files");
            string missingRoot=Path.Combine(root,"missing-index"),missingFolder=ScreenshotStorage.Folder(missingRoot);Directory.CreateDirectory(missingFolder);
            string missingImage=Path.Combine(missingFolder,Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(missingImage,[23]);
            Check(!ScreenshotStorage.Prepare(missingRoot).Info.CanClean&&File.Exists(missingImage)&&!File.Exists(Path.Combine(missingRoot,"conversations.db")),"Missing indexes must retain files without creating a replacement database");
            string corruptRoot=Path.Combine(root,"corrupt-index");Directory.CreateDirectory(ScreenshotStorage.Folder(corruptRoot));
            File.WriteAllBytes(Path.Combine(ScreenshotStorage.Folder(corruptRoot),Guid.NewGuid().ToString("N")+".png"),[24]);
            string corruptDatabase=Path.Combine(corruptRoot,"conversations.db");File.WriteAllText(corruptDatabase,"generated corrupt index");
            Check(!ScreenshotStorage.Prepare(corruptRoot).Info.CanClean&&File.ReadAllText(corruptDatabase)=="generated corrupt index"&&!Directory.EnumerateFiles(corruptRoot,"*.damaged-*").Any(),"Storage inspection must not recover or replace a damaged database");
            string backupRoot=Path.Combine(root,"backup-index");using(var backupStore=new ConversationStore(backupRoot)){backupStore.SaveImage([25]);}
            File.WriteAllText(Path.Combine(backupRoot,"conversations.db.damaged-fixture"),"generated backup");
            Check(!ScreenshotStorage.Prepare(backupRoot).Info.CanClean,"Recoverable damaged backups must block cleanup of their potentially referenced screenshots");
            foreach(string fixture in new[]{"missing-conversations","missing-preferences","uninitialized-version"})
            {
                string schemaRoot=Path.Combine(root,fixture),schemaImage;
                using(var schemaStore=new ConversationStore(schemaRoot))schemaImage=schemaStore.SaveImage([26]);
                string schemaDatabase=Path.Combine(schemaRoot,"conversations.db");
                ExecuteSql(schemaDatabase,fixture switch
                {
                    "missing-conversations"=>"DROP TABLE conversations",
                    "missing-preferences"=>"DROP TABLE preferences",
                    _=>"PRAGMA user_version=0"
                });
                byte[] originalDatabase=File.ReadAllBytes(schemaDatabase);
                Check(!ScreenshotStorage.Prepare(schemaRoot).Info.CanClean&&File.Exists(Path.Combine(ScreenshotStorage.Folder(schemaRoot),schemaImage))&&File.ReadAllBytes(schemaDatabase).SequenceEqual(originalDatabase),"Incomplete schema inspection must preserve images without recreating tables or upgrading the database: "+fixture);
            }
            string changedSchemaRoot=Path.Combine(root,"schema-lost-after-preview"),changedSchemaImage;
            using(var schemaStore=new ConversationStore(changedSchemaRoot))changedSchemaImage=schemaStore.SaveImage([27]);
            var beforeSchemaLoss=ScreenshotStorage.Prepare(changedSchemaRoot);
            Check(beforeSchemaLoss.Info.CanClean&&beforeSchemaLoss.Info.UnusedCount==1,"Schema loss fixture must first produce a valid cleanup plan");
            string changedSchemaDatabase=Path.Combine(changedSchemaRoot,"conversations.db");ExecuteSql(changedSchemaDatabase,"DROP TABLE conversations");
            byte[] changedSchemaBytes=File.ReadAllBytes(changedSchemaDatabase);bool schemaLossRefused=false;
            try{ScreenshotStorage.Clean(beforeSchemaLoss);}catch(Exception error) when(error is IOException or InvalidDataException){schemaLossRefused=true;}
            Check(schemaLossRefused&&File.Exists(Path.Combine(ScreenshotStorage.Folder(changedSchemaRoot),changedSchemaImage))&&File.ReadAllBytes(changedSchemaDatabase).SequenceEqual(changedSchemaBytes),"Schema loss after confirmation must stop cleanup without modifying the database or deleting images");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
    private static void ExecuteSql(string path,string sql)
    {
        nint database=0;
        try
        {
            int code=sqlite3_open_v2(path,out database,2|0x10000,0);
            if(code!=0)throw new Exception("Generated schema fixture could not open SQLite: "+code);
            code=sqlite3_exec(database,sql,0,0,0);
            if(code!=0)throw new Exception("Generated schema fixture SQL failed: "+code);
        }
        finally{if(database!=0)sqlite3_close_v2(database);}
    }
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out nint database,int flags,nint vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_exec(nint database,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,nint callback,nint argument,nint errorMessage);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(nint database);
}
