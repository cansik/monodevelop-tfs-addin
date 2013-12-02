//
// Microsoft.TeamFoundation.VersionControl.Client.Workspace
//
// Authors:
//	Joel Reed (joelwreed@gmail.com)
//  Ventsislav Mladenov (ventsislav.mladenov@gmail.com)
//
// Copyright (C) 2013 Joel Reed, Ventsislav Mladenov
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Linq;
using MonoDevelop.Core;
using Microsoft.TeamFoundation.VersionControl.Client.Objects;
using Microsoft.TeamFoundation.VersionControl.Client.Enums;
using MonoDevelop.Ide;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client.Enums;

namespace Microsoft.TeamFoundation.VersionControl.Client
{
    public sealed class Workspace : IEquatable<Workspace>, IComparable<Workspace>
    {
        #region Constructors

        private Workspace(string name, 
                          string ownerName, string comment, 
                          List<WorkingFolder> folders, string computer)
        {
            this.Name = name;
            this.OwnerName = ownerName;
            this.Comment = comment;
            this.Folders = folders;
            this.Computer = computer;
            PendingChanges = new List<PendingChange>();
        }

        public Workspace(RepositoryService versionControl, string name, 
                         string ownerName, string comment, 
                         List<WorkingFolder>  folders, string computer) 
            : this(name, ownerName, comment, folders, computer)
        {
            this.ProjectCollection = versionControl.Collection;
            this.VersionControlService = versionControl;
        }

        public Workspace(Microsoft.TeamFoundation.Client.ProjectCollection collection, string name, 
                         string ownerName, string comment, 
                         List<WorkingFolder>  folders, string computer)
            : this(name, ownerName, comment, folders, computer)
        {
            this.ProjectCollection = collection;
            this.VersionControlService = collection.GetService<RepositoryService>();
        }

        public Workspace(RepositoryService versionControl, WorkspaceData workspaceData) 
            : this(versionControl, workspaceData.Name, workspaceData.Owner, workspaceData.Comment, workspaceData.WorkingFolders, workspaceData.Computer)
        {
        }

        public Workspace(Microsoft.TeamFoundation.Client.ProjectCollection collection, WorkspaceData workspaceData) 
            : this(collection, workspaceData.Name, workspaceData.Owner, workspaceData.Comment, workspaceData.WorkingFolders, workspaceData.Computer)
        {
        }

        #endregion

        public CheckInResult CheckIn(List<PendingChange> changes, string comment, Dictionary<int, WorkItemCheckinAction> workItems)
        {
            foreach (var change in changes)
            {
                this.VersionControlService.UploadFile(this, change);
            }
            var result = this.VersionControlService.CheckIn(this, changes, comment, workItems);
            WorkItemManager wm = new WorkItemManager(this.ProjectCollection);
            wm.UpdateWorkItems(result.ChangeSet, workItems, comment);
            this.RefreshPendingChanges();
            ProcessGetOperations(result.LocalVersionUpdates, ProcessType.Get);
            foreach (var file in changes.Where(ch => ch.ItemType == ItemType.File && !string.IsNullOrEmpty(ch.LocalItem)).Select(ch => ch.LocalItem).Distinct())
            {
                MakeFileReadOnly(file);
            }
            return result;
        }

        #region Pending Changes

        public List<PendingChange> PendingChanges { get; set; }

        public void RefreshPendingChanges()
        {
            this.PendingChanges.Clear();
            var paths = this.Folders.Select(f => f.LocalItem).ToArray();
            this.PendingChanges.AddRange(this.GetPendingChanges(paths, RecursionType.Full));
        }

        public List<PendingChange> GetPendingChanges()
        {
            return GetPendingChanges(VersionControlPath.RootFolder, RecursionType.Full);
        }

        public List<PendingChange> GetPendingChanges(string item)
        {
            return GetPendingChanges(item, RecursionType.None);
        }

        public List<PendingChange> GetPendingChanges(string item, RecursionType rtype)
        {
            return GetPendingChanges(item, rtype, false);
        }

        public List<PendingChange> GetPendingChanges(string item, RecursionType rtype,
                                                     bool includeDownloadInfo)
        {
            string[] items = { item };
            return GetPendingChanges(items, rtype, includeDownloadInfo);
        }

        public List<PendingChange> GetPendingChanges(string[] items, RecursionType rtype)
        {
            return GetPendingChanges(items, rtype, false);
        }

        public List<PendingChange> GetPendingChanges(string[] items, RecursionType rtype,
                                                     bool includeDownloadInfo)
        {

            var itemSpecs = new List<ItemSpec>(items.Select(i => new ItemSpec(i, rtype)));
            return this.VersionControlService.QueryPendingChangesForWorkspace(this, itemSpecs, includeDownloadInfo);
        }

        public List<PendingSet> GetPendingSets(string item, RecursionType recurse)
        {
            ItemSpec[] items = { new ItemSpec(item, recurse) };
            return this.VersionControlService.QueryPendingSets(this.Name, this.OwnerName, string.Empty, string.Empty, items, false);
        }

        #endregion

        #region GetItems

        public Item GetItem(string path, ItemType itemType)
        {
            return GetItem(path, itemType, false);
        }

        public Item GetItem(string path, ItemType itemType, bool includeDownloadUrl)
        {
            var itemSpec = new ItemSpec(path, RecursionType.None);
            var items = this.VersionControlService.QueryItems(this, itemSpec, VersionSpec.Latest, DeletedState.Any, itemType, includeDownloadUrl);
            return items.SingleOrDefault();
        }

        public ExtendedItem GetExtendedItem(string path, ItemType itemType)
        {
            var itemSpec = new ItemSpec(path, RecursionType.None);
            var items = this.VersionControlService.QueryItemsExtended(this, itemSpec, DeletedState.Any, itemType);
            return items.SingleOrDefault();
        }

        public List<ExtendedItem> GetExtendedItems(List<ItemSpec> itemSpecs,
                                                   DeletedState deletedState,
                                                   ItemType itemType)
        {
            return this.VersionControlService.QueryItemsExtended(this.Name, this.OwnerName, itemSpecs, deletedState, itemType);
        }

        #endregion

        public bool IsLocalPathMapped(string localPath)
        {
            if (localPath == null)
                throw new ArgumentNullException("localPath");
            return Folders.Any(f => localPath.StartsWith(f.LocalItem, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsServerPathMapped(VersionControlPath serverPath)
        {
            return Folders.Any(f => serverPath.IsChildOrEqualTo(f.ServerItem));
        }

        public VersionControlPath TryGetServerItemForLocalItem(string localItem)
        {
            var mappedFolder = Folders.FirstOrDefault(f => localItem.StartsWith(f.LocalItem, StringComparison.OrdinalIgnoreCase));
            if (mappedFolder == null)
                return null;
            if (string.Equals(mappedFolder.LocalItem, localItem, StringComparison.OrdinalIgnoreCase))
                return mappedFolder.ServerItem;
            else
            {
                string rest = TfsPath.LocalToServerPath(localItem.Substring(mappedFolder.LocalItem.Length));
                return mappedFolder.ServerItem + rest;
            }
        }

        public string TryGetLocalItemForServerItem(VersionControlPath serverItem)
        {
            var mappedFolder = Folders.FirstOrDefault(f => serverItem.IsChildOrEqualTo(f.ServerItem));
            if (mappedFolder == null)
                return null;
            if (serverItem == mappedFolder.ServerItem)
                return mappedFolder.LocalItem;
            else
            {
                //string rest = TfsPath.ServerToLocalPath(serverItem.ToString().Substring(mappedFolder.ServerItem.ToString().Length + 1));
                string rest = TfsPath.ServerToLocalPath(serverItem.ChildPart(mappedFolder.ServerItem));
                return Path.Combine(mappedFolder.LocalItem, rest);
            }
        }

        public WorkingFolder TryGetWorkingFolderForServerItem(string serverItem)
        {
            int maxPath = 0;
            WorkingFolder workingFolder = null;

            foreach (WorkingFolder folder in Folders)
            {
                if (!serverItem.StartsWith(folder.ServerItem, StringComparison.InvariantCultureIgnoreCase))
                    continue;

                if (folder.LocalItem.Length > maxPath)
                {
                    workingFolder = folder;
                    maxPath = folder.LocalItem.Length;
                }
            }

            return workingFolder;
        }

        public void Map(string serverPath, string localPath)
        {
            this.Folders.Add(new WorkingFolder(serverPath, localPath));
            this.Update();
        }

        private void Update()
        {
            this.VersionControlService.UpdateWorkspace(this.Name, this.OwnerName, this);
        }

        #region Version Control Operations

        public GetStatus Get(GetRequest request, GetOptions options, IProgressMonitor monitor = null)
        {
            var requests = new List<GetRequest> { request };
            return Get(requests, options, monitor);
        }

        public GetStatus Get(List<GetRequest> requests, GetOptions options, IProgressMonitor monitor = null)
        {
            bool force = options.HasFlag(GetOptions.GetAll);
            bool noGet = options.HasFlag(GetOptions.Preview);

            var getOperations = this.VersionControlService.Get(this, requests, force, noGet);           
            ProcessGetOperations(getOperations, ProcessType.Get, monitor);
            return new GetStatus(getOperations.Count);
        }

        private void CollectPaths(FilePath root, List<ChangeRequest> paths)
        {
            if (!root.IsDirectory)
                return;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                paths.Add(new ChangeRequest(dir, RequestType.Add, ItemType.Folder));
                CollectPaths(dir, paths);
            }
            foreach (var file in Directory.EnumerateFiles(root))
            {
                paths.Add(new ChangeRequest(file, RequestType.Add, ItemType.File));
            }
        }

        public int PendAdd(List<FilePath> paths, bool isRecursive)
        {
            if (paths.Count == 0)
                return 0;

            List<ChangeRequest> changes = new List<ChangeRequest>();

            foreach (var path in paths)
            {
                var itemType = path.IsDirectory ? ItemType.Folder : ItemType.File;
                changes.Add(new ChangeRequest(path, RequestType.Add, itemType));
                if (isRecursive && itemType == ItemType.Folder)
                {
                    CollectPaths(path, changes);
                }
            }
            List<Failure> failures;
            var operations = this.VersionControlService.PendChanges(this, changes, out failures);
            ProcessGetOperations(operations, ProcessType.Get);
            this.RefreshPendingChanges();
            return operations.Count;
        }
        //Delete from Version Control, but don't delete file from file system - Monodevelop Logic.
        public void PendDelete(List<FilePath> paths, RecursionType recursionType)
        {
            if (paths.Count == 0)
                return;

            var changes = paths.Select(p => new ChangeRequest(p, RequestType.Delete, p.IsDirectory ? ItemType.Folder : ItemType.File, recursionType, LockLevel.None, VersionSpec.Latest)).ToList();
            List<Failure> failures;
            this.VersionControlService.PendChanges(this, changes, out failures);
            this.RefreshPendingChanges();
        }

        public List<Failure> PendEdit(List<FilePath> paths, RecursionType recursionType, CheckOutLockLevel checkOutlockLevel)
        {
            if (paths.Count == 0)
                return new List<Failure>();
            LockLevel lockLevel = LockLevel.None;
            if (checkOutlockLevel == CheckOutLockLevel.CheckOut)
                lockLevel = LockLevel.CheckOut;
            else if (checkOutlockLevel == CheckOutLockLevel.CheckIn)
                lockLevel = LockLevel.Checkin;
            var changes = paths.Select(p => new ChangeRequest(p, RequestType.Edit, ItemType.File, recursionType, lockLevel, VersionSpec.Latest)).ToList();
            List<Failure> failures;
            var getOperations = this.VersionControlService.PendChanges(this, changes, out failures);
            ProcessGetOperations(getOperations, ProcessType.Get);
            foreach (GetOperation getOperation in getOperations)
            {
                MakeFileWritable(getOperation.TargetLocalItem);
            }
            this.RefreshPendingChanges();
            return failures;
        }

        private void PendRename(string oldPath, string newPath, ItemType itemType)
        {
            List<ChangeRequest> changes = new List<ChangeRequest>();
            changes.Add(new ChangeRequest(oldPath, newPath, RequestType.Rename, itemType));
            List<Failure> failures;
            var getOperations = this.VersionControlService.PendChanges(this, changes, out failures);
            ProcessGetOperations(getOperations, ProcessType.Get);
            this.RefreshPendingChanges();
        }

        public void PendRenameFile(string oldPath, string newPath)
        {
            PendRename(oldPath, newPath, ItemType.File);
        }

        public void PendRenameFolder(string oldPath, string newPath)
        {
            PendRename(oldPath, newPath, ItemType.Folder);
        }

        public List<FilePath> Undo(List<ItemSpec> items, IProgressMonitor monitor = null)
        {
            var operations = this.VersionControlService.UndoPendChanges(this, items);
            ProcessGetOperations(operations, ProcessType.Undo, monitor);
            this.RefreshPendingChanges();
            List<FilePath> undoPaths = new List<FilePath>();
            foreach (var oper in operations)
            {
                undoPaths.Add(oper.TargetLocalItem);
            }
            return undoPaths;
        }

        public void LockFiles(List<FilePath> paths, LockLevel lockLevel)
        {
            SetLock(paths, lockLevel, RecursionType.None);
        }

        public void SetLock(List<FilePath> paths, LockLevel lockLevel, RecursionType recursion)
        {
            if (paths.Count == 0)
                return;

            var changes = paths.Select(p => new ChangeRequest(p, RequestType.Lock, p.IsDirectory ? ItemType.Folder : ItemType.File, recursion, lockLevel, VersionSpec.Latest)).ToList();
            List<Failure> failures;
            var getOperations = this.VersionControlService.PendChanges(this, changes, out failures);
            ProcessGetOperations(getOperations, ProcessType.Get);
            this.RefreshPendingChanges();
        }

        public List<Conflict> GetConflicts(IEnumerable<FilePath> paths)
        {
            var itemSpecs = paths.Select(p => new ItemSpec(p, RecursionType.Full)).ToList();
            return this.VersionControlService.QueryConflicts(this, itemSpecs);
        }

        public void Resolve(Conflict conflict, ResolutionType resolutionType)
        {
            var result = this.VersionControlService.Resolve(conflict, resolutionType);
            ProcessGetOperations(result.GetOperations, ProcessType.Get);
            this.Undo(result.UndoOperations.Select(x => new ItemSpec(x.TargetLocalItem, RecursionType.None)).ToList());
        }

        #endregion

        #region Serialization

        internal static Workspace FromXml(RepositoryService versionControl, XElement element)
        {
            string computer = element.Attribute("computer").Value;
            string name = element.Attribute("name").Value;
            string owner = element.Attribute("owner").Value;
            //bool isLocal = Convert.ToBoolean(element.Attribute("islocal").Value);

            string comment = element.Element(XmlNamespaces.GetMessageElementName("Comment")).Value;
            DateTime lastAccessDate = DateTime.Parse(element.Element(XmlNamespaces.GetMessageElementName("LastAccessDate")).Value);
            var folders = new List<WorkingFolder>(element.Element(XmlNamespaces.GetMessageElementName("Folders"))
                                                         .Elements(XmlNamespaces.GetMessageElementName("WorkingFolder"))
                                                         .Select(el => WorkingFolder.FromXml(el)));

            return new Workspace(versionControl, name, owner, comment, folders, computer)
            { 
                LastAccessDate = lastAccessDate 
            };
        }

        internal XElement ToXml(XName elementName)
        {
            var ns = elementName.Namespace;
            XElement element = new XElement(elementName, 
                                   new XAttribute("computer", Computer), 
                                   new XAttribute("name", Name),
                                   new XAttribute("owner", OwnerName), 
                                   new XElement(ns + "Comment", Comment));

            if (Folders != null)
            {
                element.Add(new XElement(ns + "Folders", Folders.Select(f => f.ToXml(ns))));
            }
            return element;
        }

        #endregion

        internal void MakeFileReadOnly(string path)
        {
            if (File.Exists(path))
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        internal void MakeFileWritable(string path)
        {
            if (File.Exists(path))
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }

        internal void UnsetDirectoryAttributes(string path)
        {
            DirectoryInfo dir = new DirectoryInfo(path);
            FileInfo[] localFiles = dir.GetFiles("*", SearchOption.AllDirectories);
            foreach (FileInfo file in localFiles)
                File.SetAttributes(file.FullName, FileAttributes.Normal);
        }

        #region Equal

        #region IComparable<Workspace> Members

        public int CompareTo(Workspace other)
        {
            var nameCompare = string.Compare(Name, other.Name, StringComparison.Ordinal);
            if (nameCompare != 0)
                return nameCompare;
            return string.Compare(OwnerName, other.OwnerName, StringComparison.Ordinal);
        }

        #endregion

        #region IEquatable<Workspace> Members

        public bool Equals(Workspace other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return string.Equals(other.Name, Name) && string.Equals(other.OwnerName, OwnerName);
        }

        #endregion

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            Workspace cast = obj as Workspace;
            if (cast == null)
                return false;
            return Equals(cast);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public static bool operator ==(Workspace left, Workspace right)
        {
            return ReferenceEquals(null, left) ? ReferenceEquals(null, right) : left.Equals(right);
        }

        public static bool operator !=(Workspace left, Workspace right)
        {
            return !(left == right);
        }

        #endregion Equal

        #region Process Get Operations

        private string DownloadFile(GetOperation operation, VersionControlDownloadService downloadService)
        {
            string path = string.IsNullOrEmpty(operation.TargetLocalItem) ? operation.SourceLocalItem : operation.TargetLocalItem;
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            if (operation.ItemType == ItemType.Folder)
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
            if (operation.ItemType == ItemType.File)
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                return downloadService.Download(path, operation.ArtifactUri);
            }
            return string.Empty;
        }

        private UpdateLocalVersion ProcessEdit(GetOperation operation, VersionControlDownloadService downloadService, ProcessType processType)
        {
            if (processType == ProcessType.Undo)
            {
                var path = DownloadFile(operation, downloadService);
                if (operation.ItemType == ItemType.File)
                    MakeFileReadOnly(path);
            }
            else
            {
                string path = string.IsNullOrEmpty(operation.TargetLocalItem) ? operation.SourceLocalItem : operation.TargetLocalItem;
                MakeFileWritable(path);
            }
            return new UpdateLocalVersion(operation.ItemId, operation.TargetLocalItem, operation.VersionServer);
        }

        private UpdateLocalVersion ProcessGet(GetOperation operation, VersionControlDownloadService downloadService)
        {
            var path = DownloadFile(operation, downloadService);
            if (operation.ItemType == ItemType.File)
                MakeFileReadOnly(path);
            return new UpdateLocalVersion(operation.ItemId, operation.TargetLocalItem, operation.VersionServer);
        }

        private UpdateLocalVersion ProcessDelete(GetOperation operation, VersionControlDownloadService downloadService, ProcessType processType)
        {
            if (processType == ProcessType.Undo)
            {
                var update = ProcessGet(operation, downloadService);
                var filePath = (FilePath)operation.TargetLocalItem;
                var projects = IdeApp.Workspace.GetAllProjects();
                foreach (var project in projects)
                {
                    if (filePath.IsChildPathOf(project.BaseDirectory))
                    {
                        if (operation.ItemType == ItemType.File)
                            project.AddFile(operation.TargetLocalItem);
                        if (operation.ItemType == ItemType.Folder)
                            project.AddDirectory(operation.TargetLocalItem.Substring(((string)project.BaseDirectory).Length + 1));
                        break;
                    }
                }
                return update;
            }
            else
                return InternalProcessDelete(operation);
        }

        private UpdateLocalVersion InternalProcessDelete(GetOperation operation)
        {
            var path = operation.SourceLocalItem;
            try
            {
                if (operation.ItemType == ItemType.File)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                else
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                }
            }
            catch
            {
                LoggingService.Log(MonoDevelop.Core.Logging.LogLevel.Info, "Can not delete path:" + path);
            }
            return new UpdateLocalVersion(operation.ItemId, null, operation.VersionServer);
        }

        private UpdateLocalVersion ProcessRename(GetOperation operation, ProcessType processType)
        {
            if (processType == ProcessType.Undo)
            {
                var filePath = (FilePath)operation.SourceLocalItem;
                var projects = MonoDevelop.Ide.IdeApp.Workspace.GetAllProjects();
                var found = false;
                foreach (var project in projects)
                {
                    if (filePath.IsChildPathOf(project.BaseDirectory))
                    {
                        found = true;
                        project.Files.Remove(operation.SourceLocalItem);
                        //Move file only on undo, let Ide do Get Process Type
                        if (operation.ItemType == ItemType.File)
                        {
                            File.Move(operation.SourceLocalItem, operation.TargetLocalItem);
                            project.AddFile(operation.TargetLocalItem);
                        }    
                        if (operation.ItemType == ItemType.Folder)
                        {
                            Directory.Move(operation.SourceLocalItem, operation.TargetLocalItem);
                            project.AddDirectory(operation.TargetLocalItem.Substring(((string)project.BaseDirectory).Length + 1));
                        }
                        break;
                    }
                }
                if (!found)
                {
                    if (operation.ItemType == ItemType.File)
                        File.Move(operation.SourceLocalItem, operation.TargetLocalItem);
                    if (operation.ItemType == ItemType.Folder)
                        Directory.Move(operation.SourceLocalItem, operation.TargetLocalItem);
                }
            }
            return new UpdateLocalVersion(operation.ItemId, operation.TargetLocalItem, operation.VersionServer);
        }

        private enum ProcessType
        {
            Get,
            Undo,
        }

        private IEnumerable<UpdateLocalVersion> InternalProcessGetOperations(List<GetOperation> getOperations, ProcessType processType, IProgressMonitor monitor)
        {
            var downloadService = this.VersionControlService.Collection.GetService<VersionControlDownloadService>();
            foreach (var operation in getOperations)
            {
                try
                {
                    string stepName = operation.ChangeType == ChangeType.None ? "Get" : operation.ChangeType.ToString();
                    if (monitor.IsCancelRequested)
                        yield break;
                    monitor.BeginTask(stepName + ": " + operation.TargetLocalItem, 1);
                    if (operation.ChangeType.HasFlag(ChangeType.Add))
                    {
                        continue; //Noting to process
                    }
                    if (operation.ChangeType.HasFlag(ChangeType.Edit))
                    {
                        yield return ProcessEdit(operation, downloadService, processType);
                        continue;
                    }
                    if (operation.ChangeType.HasFlag(ChangeType.Delete) || operation.DeletionId > 0)
                    {
                        yield return ProcessDelete(operation, downloadService, processType);
                        continue;
                    }
                    if (operation.ChangeType.HasFlag(ChangeType.Rename))
                    {
                        yield return ProcessRename(operation, processType);
                        continue;
                    }
                    if (operation.ChangeType.HasFlag(ChangeType.None))
                    {
                        yield return ProcessGet(operation, downloadService);
                    }
                }
                finally
                {
                    monitor.EndTask();
                }
            }
        }

        private void ProcessGetOperations(List<GetOperation> getOperations, ProcessType processType, IProgressMonitor monitor = null)
        {
            if (getOperations == null || getOperations.Count == 0)
                return;
            IProgressMonitor progress = monitor ?? new MonoDevelop.Ide.ProgressMonitoring.MessageDialogProgressMonitor(DispatchService.IsGuiThread, false, false);
            try
            {
                progress.BeginTask("Process", getOperations.Count);
                UpdateLocalVersionQueue updates = new UpdateLocalVersionQueue(this);
                foreach (var update in InternalProcessGetOperations(getOperations, processType, progress))
                {
                    updates.QueueUpdate(update);
                    progress.Step(1);
                }
                updates.Flush();
                progress.EndTask();
            }
            finally
            {
                if (monitor == null && progress != null)
                    progress.Dispose();
            }
        }

        #endregion

        public string GetItemContent(Item item)
        {
            if (item == null || item.ItemType == ItemType.Folder)
                return string.Empty;
            if (item.DeletionId > 0)
                return string.Empty;
            var dowloadService = this.ProjectCollection.GetService<VersionControlDownloadService>();
            var tempName = dowloadService.DownloadToTemp(item.ArtifactUri);
            var text = item.Encoding > 0 ? File.ReadAllText(tempName, Encoding.GetEncoding(item.Encoding)) :
                       File.ReadAllText(tempName);
            File.Delete(tempName);
            return text;
        }

        public string Comment { get; private set; }

        public string Computer { get; private set; }

        public List<WorkingFolder> Folders { get; private set; }

        public string Name { get; private set; }

        public DateTime LastAccessDate { get; private set; }

        public string OwnerName { get; private set; }

        public Microsoft.TeamFoundation.Client.ProjectCollection ProjectCollection { get; set; }

        public RepositoryService VersionControlService { get; set; }

        public override string ToString()
        {
            return "Owner: " + OwnerName + ", Name: " + Name;
        }
    }
}
