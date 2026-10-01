using FerrarisPOS.Data;
namespace FerrarisPOS.Services;
public static class BackupService
{
    public static string CreateBackup(){Database.Backup(); return Directory.GetFiles(Database.BackupDirectory,"FerrarisPOS_*.db").OrderByDescending(File.GetLastWriteTime).First();}
}
