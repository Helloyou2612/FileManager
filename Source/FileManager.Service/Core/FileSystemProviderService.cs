using DevExpress.Web;
using FileManager.Common.Helper;
using FileManager.Dal.EDM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace FileManager.Service.Core
{
    public class FileSystemProviderService : FileSystemProviderBase
    {
        private Dictionary<int, CacheEntry> _folderCache;

        private const int ROOT_ITEM_ID = 1;

        public FileSystemProviderService(string rootFolder)
        : base(rootFolder)
        {
            RefreshCache();
        }

        public override string RootFolderDisplayName
        {
            get
            {
                if (_folderCache == null)
                    RefreshCache();
                return _folderCache.Values.FirstOrDefault(x => x.Item.ParentID == -1).Item.Name;
            }
        }

        //test
        public override void GetFilteredItems(FileManagerGetFilteredItemsArgs args)
        {
            var test = args.FileListCustomFilter;
        }

        public override Stream ReadFile(FileManagerFile file)
        {
            var docID = Convert.ToInt32(file.Id);
            var currentDoc = _fileDataService.Get(x => x.ID == docID);

            return FileHelper.GetBytesAsStream(currentDoc.FileData);
        }

        #region GetFolders / GetFiles

        public override IEnumerable<FileManagerFolder> GetFolders(FileManagerFolder parentFolder)
        {
            var folderItem = FindDocFolderItem(parentFolder);
            return _folderCache.Values
                    .Where(x => x.Item.IsFolder && !x.Item.IsDeleted && x.Item.ParentID == folderItem.Item.ID)
                    .OrderByDescending(x => x.Item.PromulgatedDate).ThenByDescending(x => x.Item.EffectiveFrom)
                    .Select(x => new FileManagerFolder(this, parentFolder, x.Item.Name, x.Item.ID.ToString(),
                    new FileManagerFolderProperties
                    {
                        Permissions = GetFolderPermissionsInternal(x.Permissions)
                    }));
        }

        public override IEnumerable<FileManagerFile> GetFiles(FileManagerFolder folder)
        {
            if (HttpContext.Current.Session["searchAgain"] != null)
            {
                if (folder.Id == "" && (int)HttpContext.Current.Session["searchAgain"] == 1)
                {
                    if (HttpContext.Current.Session["CurrentFolder"] != null)
                    {
                        folder = (FileManagerFolder)HttpContext.Current.Session["CurrentFolder"];
                        HttpContext.Current.Session["CurrentFolder"] = null;
                    }
                }
            }
            int saveFolder = HttpContext.Current.Session["searchAgain"] == null ? 0 : (int)HttpContext.Current.Session["searchAgain"];
            if (saveFolder != 1)
                HttpContext.Current.Session["CurrentFolder"] = folder;
            var docFolder = FindDocFolderItem(folder);

            List<FileViewModel> files = null;
            HttpContext.Current.Session["Files"] = null;

            try
            {
                // trong truong hop ko co filter , chi lay con 1 nac
                if (SearchModel == null)
                {
                    //Chỉ lấy các trường cần thiết
                    files = _filePermissionManager.GetFilesByPermissionFirstLayer(_currentUser, docFolder.Item, (short)Enums.DocumentTypeCategory.ALL, Enums.PermissionCode.Read)
                        .OrderByDescending(x => x.PromulgatedDate).ThenByDescending(x => x.EffectiveFrom)
                        .ToList();
                    //.Select(x => new FileManagerFile(this, folder, x.Name, x.ID.ToString()));
                }

                //su dung custom filter => lay tat ca con co cung tinh chat va su dung permission read
                else
                {
                    var data = _filePermissionManager.GetFilesByPermission(_currentUser, docFolder.Item, (short)Enums.DocumentTypeCategory.ALL, Enums.PermissionCode.Read);
                    var filteredData = SearchModel.Filter(data);
                    //chỉ lấy các trường cần thiết
                    files = filteredData.OrderByDescending(x => x.PromulgatedDate).ThenByDescending(x => x.EffectiveFrom).ToList();
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
            HttpContext.Current.Session["Files"] = files;

            //Đổi lại thumnail cho những file outdated:
            //Get danh sach id cac file đã bị thay the
            var replacedByFiles = _filePermissionManager.GetAllDocumentReplaced((short)Enums.ReplacedType.Replaced).Select(x => x.ReplacedByID).Distinct().ToList();

            //Get danh sach id cac file đã bị thay the
            var replacedFiles = _filePermissionManager.GetAllDocumentReplaced((short)Enums.ReplacedType.Replaced).Select(x => x.ReplacedID).Distinct().ToList();

            //Get danh sach id cac file sửa đổi bổ sung
            var additionalFiles = _filePermissionManager.GetAllDocumentReplaced((short)Enums.ReplacedType.Supplemented).Select(x => x.ReplacedByID).Distinct().ToList();

            //Get danh sach id cac file bị sửa đổi bổ sung
            var supplementedFiles = _filePermissionManager.GetAllDocumentReplaced((short)Enums.ReplacedType.Supplemented).Select(x => x.ReplacedID).Distinct().ToList();

            foreach (var item in files)
            {
                var properties = new FileManagerFileProperties();

                //thứ tự sẽ hiển thị là văn bản bị thay thế, văn bản bị bổ sung, văn bản thay thế, văn bản bổ sung, VB Hiện hữu, VB Hết hiệu lực
                if (item.EffectiveTo.HasValue)
                {
                    if (item.EffectiveTo < DateTime.Now)
                        item.FileType = "VB Hết hiệu lực";
                    else item.FileType = "VB Hiện hữu";
                }

                if (!item.EffectiveTo.HasValue)
                    item.FileType = "VB Hiện hữu";

                if (additionalFiles.Contains(item.ID))
                {
                    //properties.ThumbnailUrl = SSIConfig.File_Icon_Additional;
                    //properties.TooltipText = "Văn bản sửa đổi bổ sung";
                    item.FileType = "VB Sửa đổi bổ sung";
                }
                if (replacedByFiles.Contains(item.ID))
                {
                    //properties.ThumbnailUrl = SSIConfig.File_Icon_ReplacedBy;
                    //properties.TooltipText = "Văn bản thay thế";
                    item.FileType = "VB Thay thế";
                }
                if (supplementedFiles.Contains(item.ID))
                {
                    //properties.ThumbnailUrl = SSIConfig.File_Icon_Supplemented;
                    //properties.TooltipText = "Văn bản bị sửa đổi bổ sung";
                    item.FileType = "VB Bị sửa đổi bổ sung";
                }
                if (replacedFiles.Contains(item.ID))
                {
                    //properties.ThumbnailUrl = SSIConfig.File_Icon_Replaced;
                    //properties.TooltipText = "Văn bản bị thay thế";
                    item.FileType = "VB Bị thay thế";
                }

                var fileManagerFile = new FileManagerFile(this, folder, item.Name, item.ID.ToString(), properties);
                yield return fileManagerFile;
            }
        }

        #endregion GetFolders / GetFiles

        #region CreateFolder/ UploadFile

        public override void CreateFolder(FileManagerFolder parent, string name)
        {
            string err = string.Empty;
            try
            {
                var parentFolder = FindDocFolderItem(parent);

                //var canWrite = _filePermissionManager.HasPermission(_currentUser, parentFolder.Item, PermissionCode.Write, out err);
                //if (!canWrite)
                //{
                //    throw new UnauthorizedAccessException(err);
                //}

                var folder = new FileDocument
                {
                    ParentID = parentFolder.Item.ID,
                    Name = name,
                    FileData = null,
                    IsFolder = true,
                    LastWriteTime = DateTime.Now,
                    Department = _currentUser.DepartmentId,
                    IsPublic = parentFolder.Item.IsPublic,
                    CreatedDate = DateTime.Now,
                    CreatedByUser = _currentUser.UserName,
                };

                _fileDataService.AddNode(folder);
                _fileDataService.SaveChanges();

                #region file/forder permissions

                if (!CustomPermissionsAdmin(folder))
                    throw new Exception("Có lỗi thêm quyền cho Admin.");

                var lstDepart = _filePermissionManager.GetAllDepartment().ToList();
                foreach (var item in lstDepart)
                    if (!CustomPermissions(item, folder))
                        throw new Exception("Có lỗi thêm quyền cho các phòng ban.");

                #endregion file/forder permissions

                RefreshCache();
            }
            catch (DbEntityValidationException e)
            {
                foreach (var eve in e.EntityValidationErrors)
                {
                    Console.WriteLine("Entity of type \"{0}\" in state \"{1}\" has the following validation errors:",
                        eve.Entry.Entity.GetType().Name, eve.Entry.State);
                    foreach (var ve in eve.ValidationErrors)
                    {
                        Console.WriteLine("- Property: \"{0}\", Error: \"{1}\"",
                            ve.PropertyName, ve.ErrorMessage);
                        err += "- Property: " + ve.PropertyName + ", Error: " + ve.ErrorMessage + ", ";
                    }
                }
                err += e.Message;
                _log.FatalFormat("{0} : has DbEntityValidationException {1}", MethodBase.GetCurrentMethod(), err);
                throw new Exception("Có lỗi xẩy ra.");
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has DbEntityValidationException {1}", MethodBase.GetCurrentMethod(), ex);
                throw new Exception("Có lỗi xẩy ra.");
            }
        }

        public override void UploadFile(FileManagerFolder folder, string fileName, Stream content)
        {
            string err = string.Empty;
            try
            {
                #region collection data

                var fileNo = HttpContext.Current.Request["hddDocFileNo"];
                var docName = HttpContext.Current.Request["hddDocName"];
                var mimeType = MimeMapping.GetMimeMapping(docName);
                var extension = Path.GetExtension(docName);
                //var departmentIdStr = HttpContext.Current.Request["hddDepartmentId"];
                //var departmentId = string.IsNullOrEmpty(departmentIdStr) ? 0 : Convert.ToInt32(departmentIdStr);

                var departmentId = _currentUser.DepartmentId;

                var promulgatedDateStr = HttpContext.Current.Request["hddPromulgatedDate"];
                var promulgatedDate = string.IsNullOrEmpty(promulgatedDateStr) ? (DateTime?)null : DateTime.Parse(promulgatedDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(1);

                var description = HttpContext.Current.Request["hddDescription"];

                var replacedByIdStr = HttpContext.Current.Request["hddReplacedBy"];
                var replacedById = String.IsNullOrEmpty(replacedByIdStr) ? 0 : Convert.ToInt32(replacedByIdStr);

                //var replaceIdStr = HttpContext.Current.Request["hddReplace"];
                //var replaceId = String.IsNullOrEmpty(replaceIdStr) ? 0 : Convert.ToInt32(replaceIdStr);

                string[] arr_ReplaceId = null;
                var replacedIDList = HttpContext.Current.Request["hddReplaced"];
                if (!string.IsNullOrEmpty(replacedIDList))
                {
                    if (replacedIDList.Contains(','))
                        arr_ReplaceId = replacedIDList.Split(',').Select(s => s).ToArray();
                    else arr_ReplaceId = new[] { replacedIDList };
                }
                string[] arr_AdditionalId = null;
                var additionalIDList = HttpContext.Current.Request["hddAdditional"];
                if (!string.IsNullOrEmpty(additionalIDList))
                {
                    if (additionalIDList.Contains(','))
                        arr_AdditionalId = additionalIDList.Split(',').Select(s => s).ToArray();
                    else arr_AdditionalId = new[] { additionalIDList };
                }
                string[] arr_SupplementedId = null;
                var supplementedList = HttpContext.Current.Request["hddSupplemented"];
                if (!string.IsNullOrEmpty(supplementedList))
                {
                    if (supplementedList.Contains(','))
                        arr_SupplementedId = supplementedList.Split(',').Select(s => s).ToArray();
                    else arr_SupplementedId = new[] { supplementedList };
                }
                var isReplace = (string.IsNullOrEmpty(replacedIDList)
                                 && string.IsNullOrEmpty(additionalIDList)
                                 && string.IsNullOrEmpty(supplementedList)) ? false : true;

                var isPublicStr = HttpContext.Current.Request["hddIsPublic"];
                var isPublic = string.IsNullOrEmpty(isPublicStr) ? false : Convert.ToBoolean(isPublicStr);

                string[] arr_DepartmentPermissionId = null;
                var departmentPermissionList = HttpContext.Current.Request["hddDepartmentPermission"];
                if (!string.IsNullOrEmpty(departmentPermissionList))
                {
                    if (departmentPermissionList.Contains(','))
                        arr_DepartmentPermissionId = departmentPermissionList.Split(',').Select(s => s).ToArray();
                    else arr_DepartmentPermissionId = new[] { departmentPermissionList };
                }

                string[] arr_UserPermissionId = null;
                var userPermissionList = HttpContext.Current.Request["hddUserPermission"];
                if (!string.IsNullOrEmpty(userPermissionList))
                {
                    if (userPermissionList.Contains(','))
                        arr_UserPermissionId = userPermissionList.Split(',').Select(s => s).ToArray();
                    else arr_UserPermissionId = new[] { userPermissionList };
                }

                var documentCategoryTypeStr = HttpContext.Current.Request["hddDocumentTypeCategory"];
                var documentCategoryType = string.IsNullOrEmpty(documentCategoryTypeStr) ? 0 : Convert.ToInt32(documentCategoryTypeStr);
                var documentTypeCategoryDiff = HttpContext.Current.Request["hddDocumentTypeCategoryDiff"];

                var documentTypeStr = HttpContext.Current.Request["hddDocumentType"];
                var documentType = string.IsNullOrEmpty(documentTypeStr) ? 0 : Convert.ToInt32(documentTypeStr);
                var documentTypeDiff = HttpContext.Current.Request["hddDocumentTypeDiff"];

                var agencyTypeStr = HttpContext.Current.Request["hddAgencyType"];
                var agencyType = string.IsNullOrEmpty(agencyTypeStr) ? 0 : Convert.ToInt32(agencyTypeStr);
                var agencyTypeDiff = HttpContext.Current.Request["hddAgencyTypeDiff"];

                var dateFromStr = HttpContext.Current.Request["hddEffectiveFrom"];
                var dateFrom = string.IsNullOrEmpty(dateFromStr) ? (DateTime?)null : DateTime.Parse(dateFromStr, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(1);

                var dateToStr = HttpContext.Current.Request["hddEffectiveTo"];
                var dateTo = string.IsNullOrEmpty(dateToStr) ? (DateTime?)null : DateTime.Parse(dateToStr, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(1);

                var isDispatch = (documentCategoryType == 3 || documentCategoryType == 4) ? true : false;

                //For dispatch
                var receiveDateStr = HttpContext.Current.Request["hddReceiveDate"];
                var receiveDate = string.IsNullOrEmpty(receiveDateStr) ? (DateTime?)null : DateTime.Parse(receiveDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(1);

                string[] arr_toAgencyId = null;
                var toAgencyIdList = HttpContext.Current.Request["hddToAgency"];
                if (!string.IsNullOrEmpty(toAgencyIdList))
                {
                    if (toAgencyIdList.Contains(','))
                        arr_toAgencyId = toAgencyIdList.Split(',').Select(s => s).ToArray();
                    else arr_toAgencyId = new[] { toAgencyIdList };
                }

                //var toAgencyStr = HttpContext.Current.Request["hddToAgency"];
                //var toAgency = String.IsNullOrEmpty(toAgencyStr) ? 0 : Convert.ToInt32(toAgencyStr);

                string[] arr_answerAgencyId = null;
                var answerAgencyIdList = HttpContext.Current.Request["hddAnswerAgency"];
                if (!string.IsNullOrEmpty(answerAgencyIdList))
                {
                    if (answerAgencyIdList.Contains(','))
                        arr_answerAgencyId = answerAgencyIdList.Split(',').Select(s => s).ToArray();
                    else arr_answerAgencyId = new[] { answerAgencyIdList };
                }

                //var answerAgencyStr = HttpContext.Current.Request["hddAnswerAgency"];
                //var answerAgency = String.IsNullOrEmpty(answerAgencyStr) ? 0 : Convert.ToInt32(answerAgencyStr);

                var answerDateStr = HttpContext.Current.Request["hddAnswerDate"];
                var answerDate = string.IsNullOrEmpty(answerDateStr) ? (DateTime?)null : DateTime.Parse(answerDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(1);

                var reasonStr = HttpContext.Current.Request["hddReason"];
                var reason = String.IsNullOrEmpty(reasonStr) ? 0 : Convert.ToInt32(reasonStr);

                var reasonDiff = HttpContext.Current.Request["hddReasonDiff"];

                var statusStr = HttpContext.Current.Request["hddStatus"];
                var status = String.IsNullOrEmpty(statusStr) ? 0 : Convert.ToInt32(statusStr);

                string[] arr_answerForDispatchID = null;
                var answerForDispatchIDList = HttpContext.Current.Request["hddAnswerForDispatchID"];
                if (!string.IsNullOrEmpty(answerForDispatchIDList))
                {
                    if (answerForDispatchIDList.Contains(','))
                        arr_answerForDispatchID = answerForDispatchIDList.Split(',').Select(s => s).ToArray();
                    else arr_answerForDispatchID = new[] { answerForDispatchIDList };
                }

                //var answerForDispatchIDStr = HttpContext.Current.Request["hddAnswerForDispatchID"];
                //var answerForDispatchID = string.IsNullOrEmpty(answerForDispatchIDStr) ? 0 : Convert.ToInt32(answerForDispatchIDStr);

                var ssiSigner = HttpContext.Current.Request["hddSSISigner"];
                var receiveAgencyStr = HttpContext.Current.Request["hddReceiveAgency"];
                var receiveAgency = string.IsNullOrEmpty(receiveAgencyStr) ? 0 : Convert.ToInt32(receiveAgencyStr);

                var receiveAgencyDiff = HttpContext.Current.Request["hddReceiveAgencyDiff"];

                var sendDateStr = HttpContext.Current.Request["hddSendDate"];
                var sendDate = string.IsNullOrEmpty(sendDateStr) ? (DateTime?)null : DateTime.Parse(sendDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(1);

                var sendMethodStr = HttpContext.Current.Request["hddSendMethod"];
                var sendMethod = string.IsNullOrEmpty(sendMethodStr) ? 0 : Convert.ToInt32(sendMethodStr);

                var data = FileHelper.GetStreamAsBytes(content);
                var docFolder = FindDocFolderItem(folder);

                //là folder chi chưa file public không cho set private
                string[] FolderPublic = null;
                if (SSIConfig.FolderPublic.Contains('|'))
                    FolderPublic = SSIConfig.FolderPublic.Split(',').Select(s => s).ToArray();
                else FolderPublic = new[] { SSIConfig.FolderPublic };

                if (FolderPublic.Contains(docFolder.Item.Path.Substring(0, 4)) || documentCategoryType == (short)Enums.DocumentTypeCategory.VBPL)
                    isPublic = true;

                #endregion collection data

                #region Validate data

                //CHẶN nhập số văn bản đã có trong hệ thống
                var fileNo_Exist = _fileDataService.GetAll().Where(x => x.FileNo == fileNo && !x.IsDeleted).ToList();
                if (fileNo_Exist.Count > 0)
                    throw new Exception("Số văn bản " + fileNo + " đã tồn tại.");

                #endregion Validate data

                #region Add file

                var file = new FileDocument
                {
                    ParentID = docFolder.Item.ID,
                    FileNo = fileNo,
                    Extension = extension,
                    MimeType = mimeType,
                    Name = docName,
                    FileData = data,
                    FileSize = data.Length,
                    Department = _currentUser.DepartmentId,
                    PromulgatedDate = promulgatedDate,
                    EffectiveFrom = dateFrom,
                    EffectiveTo = dateTo,
                    DocumentTypeCategory = documentCategoryType,
                    DocumentTypeCategoryDiff = documentTypeCategoryDiff,
                    DocumentType = documentType,
                    DocumentTypeDiff = documentTypeDiff,
                    AgencyType = agencyType,
                    AgencyTypeDiff = agencyTypeDiff,
                    IsFolder = false,
                    IsPublic = isPublic,
                    //ReplacedByID = replacedById,
                    //ReplaceID = replaceId,
                    IsDispatch = isDispatch,
                    IsReplace = isReplace,
                    ReplacedIdList = arr_ReplaceId,
                    AdditionalIdList = arr_AdditionalId,
                    SupplementedIdList = arr_SupplementedId,
                    Description = description,
                    LastWriteTime = DateTime.Now,
                    CreatedDate = DateTime.Now,
                    CreatedByUser = _currentUser.UserName,
                };

                //var canWrite = _filePermissionManager.HasPermission(_currentUser, file, PermissionCode.Write, out err);

                //if (!canWrite)
                //{
                //    throw new UnauthorizedAccessException(err);
                //}

                var docNew = _fileDataService.AddNode(file);
                var docNewId = docNew.ID;

                #endregion Add file

                #region file/forder permissions

                if (!CustomPermissionsAdmin(docNew))
                    throw new Exception("Có lỗi thêm quyền cho Admin.");

                var lstDepart = _filePermissionManager.GetAllDepartment().ToList();
                foreach (var item in lstDepart)
                    if (!CustomPermissions(item, docNew))
                        throw new Exception("Có lỗi thêm quyền cho các phòng ban.");

                #endregion file/forder permissions

                #region Department Permission

                if (arr_DepartmentPermissionId != null && arr_DepartmentPermissionId.Length > 0)
                {
                    //Add all
                    List<int> idepartmentIds = new List<int>();
                    foreach (var sitem in arr_DepartmentPermissionId)
                        if (!string.IsNullOrEmpty(sitem)) idepartmentIds.Add(Int32.Parse(sitem));

                    string mesg = "";
                    if (!_filePermissionManager.AddDepartmentPermissionRange(docNewId, idepartmentIds.ToArray(), out mesg))
                        throw new Exception("Có lỗi khi thêm thông tin chia sẻ tới phòng ban");
                }

                #endregion Department Permission

                #region User Permission

                if (arr_UserPermissionId != null && arr_UserPermissionId.Length > 0)
                {
                    //Add all
                    List<Guid> iuserIds = new List<Guid>();
                    foreach (var sitem in arr_UserPermissionId)
                        if (!string.IsNullOrEmpty(sitem)) iuserIds.Add(Guid.Parse(sitem));

                    string mesg = "";
                    if (!_filePermissionManager.AddUserPermissionRange(docNewId, iuserIds.ToArray(), out mesg))
                        throw new Exception("Có lỗi khi thêm thông tin chia sẻ tới nhân viên");
                }

                #endregion User Permission

                #region Replace Files

                if (isReplace)
                {
                    //luu thong tin cho van ban bị thay the (ReplacedIdList) boi van ban (ID)
                    //Replaced
                    if (arr_ReplaceId != null && arr_ReplaceId.Length > 0)
                    {
                        //Add all
                        foreach (var iarr in arr_ReplaceId)
                        {
                            int ite = Int32.Parse(iarr);
                            var documentReplaced = new DocumentReplaced
                            {
                                ReplacedByID = docNewId,
                                ReplacedID = ite,
                                ReplaceType = (short)Enums.ReplacedType.Replaced,
                            };
                            if (!_filePermissionManager.AddDocumentReplaced(documentReplaced))
                                throw new Exception("Có lỗi khi thêm thông tin cho công văn");

                            //update IsRepalce cho các văn bản được chọn
                            var docReplace = _fileDataService.Get(x => x.ID == ite);
                            docReplace.IsReplace = true;

                            _fileDataService.Update(docReplace);
                            _fileDataService.SaveChanges();
                        }
                    }
                    ////Additional
                    //if (arr_AdditionalId != null && arr_AdditionalId.Length > 0)
                    //{
                    //    //Add all
                    //    foreach (var iarr in arr_AdditionalId)
                    //    {
                    //        int ite = Int32.Parse(iarr);
                    //        var documentReplaced = new DocumentReplaced
                    //        {
                    //            ReplacedByID = docNewId,
                    //            ReplacedID = ite,
                    //            ReplaceType = (short)Variable.ReplacedType.Additional,
                    //        };
                    //        if (!_filePermissionManager.AddDocumentReplaced(documentReplaced))
                    //            throw new Exception("Có lỗi khi thêm thông tin cho công văn");
                    //    }
                    //}
                    //Supplemented
                    if (arr_SupplementedId != null && arr_SupplementedId.Length > 0)
                    {
                        //Add all
                        foreach (var iarr in arr_SupplementedId)
                        {
                            int ite = Int32.Parse(iarr);
                            var documentReplaced = new DocumentReplaced
                            {
                                ReplacedByID = docNewId,
                                ReplacedID = ite,
                                ReplaceType = (short)Enums.ReplacedType.Supplemented,
                            };
                            if (!_filePermissionManager.AddDocumentReplaced(documentReplaced))
                                throw new Exception("Có lỗi khi thêm thông tin cho công văn");

                            //update IsRepalce cho các văn bản được chọn
                            var docReplace = _fileDataService.Get(x => x.ID == ite);
                            docReplace.IsReplace = true;

                            _fileDataService.Update(docReplace);
                            _fileDataService.SaveChanges();
                        }
                    }
                }

                #endregion Replace Files

                #region for dispatch

                if (isDispatch)
                {
                    var fk_FileDocumentID = docNewId;
                    if (docNew.DocumentTypeCategory == (short)Enums.DocumentTypeCategory.CVDen)//cong van den
                    {
                        //Add dispactch Send
                        var dispatch = new Dispatch
                        {
                            FK_FileDocumentID = fk_FileDocumentID,
                            ReceiveDate = receiveDate,
                            AnswerDate = answerDate,
                            Status = status,
                            Reason = reason,
                            ReasonDiff = reasonDiff,
                            SSISigner = ssiSigner,
                            AnswerForDispatchID = null,
                            ReceiveAgency = receiveAgency,
                            ReceiveAgencyDiff = receiveAgencyDiff,
                            SendDate = sendDate,
                            SendMethod = sendMethod,
                        };

                        if (!_filePermissionManager.AddDispatch(dispatch))
                            throw new Exception("Có lỗi khi thêm thông tin cho công văn");
                        else
                        {
                            //Thêm thông tin DispatchAgency
                            //ToAgency
                            if (arr_toAgencyId != null && arr_toAgencyId.Length > 0)
                            {
                                //Add all
                                foreach (var iarrId in arr_toAgencyId)
                                {
                                    int iteId = int.Parse(iarrId);
                                    var dispatchAgency = new DispatchAgency
                                    {
                                        FK_FileDocumentID = fk_FileDocumentID,
                                        Agency = iteId,
                                        Type = (short)Enums.DispatchAgency.ToAgency,
                                    };
                                    if (!_filePermissionManager.AddDispatchAgency(dispatchAgency))
                                        throw new Exception("Có lỗi khi thêm thông tin cho công văn");
                                }
                            }

                            //AnswerAgency
                            if (arr_answerAgencyId != null && arr_answerAgencyId.Length > 0)
                            {
                                //Add all
                                foreach (var iarrId in arr_answerAgencyId)
                                {
                                    int iteId = int.Parse(iarrId);
                                    var dispatchAgency = new DispatchAgency
                                    {
                                        FK_FileDocumentID = fk_FileDocumentID,
                                        Agency = iteId,
                                        Type = (short)Enums.DispatchAgency.AnswerAgency,
                                    };
                                    if (!_filePermissionManager.AddDispatchAgency(dispatchAgency))
                                        throw new Exception("Có lỗi khi thêm thông tin cho công văn");
                                }
                            }
                        }
                    }
                    else if (docNew.DocumentTypeCategory == (short)Enums.DocumentTypeCategory.CVDi)//cong van di
                    {
                        int ite = 0;
                        if (arr_answerForDispatchID != null && arr_answerForDispatchID.Length > 0)
                        {
                            //Chỉ có 1 công văn đến được trả lời
                            ite = int.Parse(arr_answerForDispatchID[0]);

                            //Update dispactch Recived -> update status của công văn đến được trả lời thành 'Đã xử lý'
                            var result = _dispatchs.UpdateStatus(ite, (int)Enums.DispatchStatus.DaXuLy, out err);
                            //var docDispatchReviced = db.Dispatches.Where(m => m.FK_FileDocumentID == ite).FirstOrDefault();

                            //if (docDispatchReviced != null)
                            //{
                            //    docDispatchReviced.Status = (int)Variable.DispatchStatus.DaXuLy;
                            //    db.SaveChanges();
                            //}
                        }

                        //Add dispactch Send
                        var dispatch = new Dispatch
                        {
                            FK_FileDocumentID = fk_FileDocumentID,
                            ReceiveDate = receiveDate,
                            //ToAgency = toAgency,
                            //AnswerAgency = answerAgency,
                            AnswerDate = answerDate,
                            Status = status,
                            Reason = reason,
                            ReasonDiff = reasonDiff,
                            SSISigner = ssiSigner,
                            AnswerForDispatchID = ite,
                            ReceiveAgency = receiveAgency,
                            ReceiveAgencyDiff = receiveAgencyDiff,
                            SendDate = sendDate,
                            SendMethod = sendMethod,
                        };
                        if (!_filePermissionManager.AddDispatch(dispatch))
                            throw new Exception("Có lỗi khi thêm thông tin cho công văn");
                    }
                }

                #endregion for dispatch

                _log.FatalFormat("{0} : Step 6: Save dispatch file after Upload by username: {1}", MethodBase.GetCurrentMethod(), _currentUser.UserName);

                RefreshCache();
            }
            catch (DbEntityValidationException e)
            {
                foreach (var eve in e.EntityValidationErrors)
                {
                    Console.WriteLine("Entity of type \"{0}\" in state \"{1}\" has the following validation errors:",
                        eve.Entry.Entity.GetType().Name, eve.Entry.State);
                    foreach (var ve in eve.ValidationErrors)
                    {
                        Console.WriteLine("- Property: \"{0}\", Error: \"{1}\"",
                            ve.PropertyName, ve.ErrorMessage);
                        err += "- Property: " + ve.PropertyName + ", Error: " + ve.ErrorMessage + ", ";
                    }
                }
                err += e.Message;
                _log.FatalFormat("{0} : File Name {1} upload to folder Id {2}, has DbEntityValidationException {3}", MethodBase.GetCurrentMethod(), fileName, folder.Id, err);
                throw new Exception("Có lỗi xẩy ra.");
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : File Name {1}  upload to folder Id {2}, has exception {3}", MethodBase.GetCurrentMethod(), fileName, folder.Id, ex);
                throw new Exception("Có lỗi xẩy ra.");
            }
        }

        #endregion CreateFolder/ UploadFile

        #region Access Rules

        #region set-file-folder-permissions

        public override FileManagerFilePermissions GetFilePermissions(FileManagerFile file)
        {
            int fileId = int.Parse(file.Id);
            if (_currentUser.UserType == (short)Enums.UserType.OnlyView)
            {
                var permissionsOnlyView = _fileDocumentPermissionsOnlyView.GetFileDocumentPermisstionsByFileId(fileId, _currentUser.DepartmentId);
                if (permissionsOnlyView == null)
                    return FileManagerFilePermissions.Default;
                return GetFilePermissionsInternal(permissionsOnlyView);
            }
            else
            {
                var permissions = _fileDocumentPermissions.GetFileDocumentPermisstionsByFileId(fileId, _currentUser.DepartmentId);
                if (permissions == null)
                    return FileManagerFilePermissions.Default;
                return GetFilePermissionsInternal(permissions);
            }
        }

        public override FileManagerFolderPermissions GetFolderPermissions(FileManagerFolder folder)
        {
            if (string.IsNullOrEmpty(folder.RelativeName))
                return FileManagerFolderPermissions.Default;
            if (folder.Id.Contains("aspxFMCreateHelperFolder"))
            {
                var permissions = new FileDocumentPermissionsOnlyView();
                permissions.Rename = false;
                permissions.Move = false;
                permissions.Copy = false;
                permissions.Delete = false;
                permissions.Download = false;
                permissions.Create = false;
                permissions.Upload = false;
                permissions.MoveOrCopyInto = false;
                return GetFolderPermissionsInternal(permissions);
            }
            int folderId = int.Parse(folder.Id);
            if (_currentUser.UserType == (short)Enums.UserType.OnlyView)
            {
                var permissionsOnlyView = _fileDocumentPermissionsOnlyView.GetFileDocumentPermisstionsByFolderId(folderId, _currentUser.DepartmentId);
                if (permissionsOnlyView == null)
                    return FileManagerFolderPermissions.Default;
                return GetFolderPermissionsInternal(permissionsOnlyView);
            }
            else
            {
                var permissions = _fileDocumentPermissions.GetFileDocumentPermisstionsByFolderId(folderId, _currentUser.DepartmentId);
                if (permissions == null)
                    return FileManagerFolderPermissions.Default;
                return GetFolderPermissionsInternal(permissions);
            }
        }

        #endregion set-file-folder-permissions

        #region custom-filesystem-provider-and-set-file-folder-permissions

        //Refer: https://github.com/DevExpress-Examples/aspxfilemanager-implement-custom-filesystem-provider-and-set-file-folder-permissions-t554282

        #region Get FilePermissions action custom follow rules

        private FileManagerFilePermissions GetFilePermissionsInternal(FileDocumentPermission permissions)
        {
            return (permissions.Rename ? FileManagerFilePermissions.Rename : FileManagerFilePermissions.Default)
                | (permissions.Move ? FileManagerFilePermissions.Move : FileManagerFilePermissions.Default)
                | (permissions.Copy ? FileManagerFilePermissions.Copy : FileManagerFilePermissions.Default)
                | (permissions.Delete ? FileManagerFilePermissions.Delete : FileManagerFilePermissions.Default)
                | (permissions.Download ? FileManagerFilePermissions.Download : FileManagerFilePermissions.Default);
        }

        private FileManagerFolderPermissions GetFolderPermissionsInternal(FileDocumentPermission permissions)
        {
            return (permissions.Rename ? FileManagerFolderPermissions.Rename : FileManagerFolderPermissions.Default)
                | (permissions.Move ? FileManagerFolderPermissions.Move : FileManagerFolderPermissions.Default)
                | (permissions.Copy ? FileManagerFolderPermissions.Copy : FileManagerFolderPermissions.Default)
                | (permissions.Delete ? FileManagerFolderPermissions.Delete : FileManagerFolderPermissions.Default)
                | (permissions.Create ? FileManagerFolderPermissions.Create : FileManagerFolderPermissions.Default)
                | (permissions.Upload ? FileManagerFolderPermissions.Upload : FileManagerFolderPermissions.Default)
                | (permissions.MoveOrCopyInto ? FileManagerFolderPermissions.MoveOrCopyInto : FileManagerFolderPermissions.Default);
        }

        #endregion Get FilePermissions action custom follow rules

        #region Get FilePermissions OnlyView

        private FileManagerFilePermissions GetFilePermissionsInternal(FileDocumentPermissionsOnlyView permissions)
        {
            return (permissions.Rename ? FileManagerFilePermissions.Rename : FileManagerFilePermissions.Default)
                | (permissions.Move ? FileManagerFilePermissions.Move : FileManagerFilePermissions.Default)
                | (permissions.Copy ? FileManagerFilePermissions.Copy : FileManagerFilePermissions.Default)
                | (permissions.Delete ? FileManagerFilePermissions.Delete : FileManagerFilePermissions.Default)
                | (permissions.Download ? FileManagerFilePermissions.Download : FileManagerFilePermissions.Default);
        }

        private FileManagerFolderPermissions GetFolderPermissionsInternal(FileDocumentPermissionsOnlyView permissions)
        {
            return (permissions.Rename ? FileManagerFolderPermissions.Rename : FileManagerFolderPermissions.Default)
                | (permissions.Move ? FileManagerFolderPermissions.Move : FileManagerFolderPermissions.Default)
                | (permissions.Copy ? FileManagerFolderPermissions.Copy : FileManagerFolderPermissions.Default)
                | (permissions.Delete ? FileManagerFolderPermissions.Delete : FileManagerFolderPermissions.Default)
                | (permissions.Create ? FileManagerFolderPermissions.Create : FileManagerFolderPermissions.Default)
                | (permissions.Upload ? FileManagerFolderPermissions.Upload : FileManagerFolderPermissions.Default)
                | (permissions.MoveOrCopyInto ? FileManagerFolderPermissions.MoveOrCopyInto : FileManagerFolderPermissions.Default);
        }

        #endregion Get FilePermissions OnlyView

        #region Custom Permissions

        public bool CustomPermissionsAdmin(FileDocument docNew)
        {
            try
            {
                //save to db
                var permissions = new FileDocumentPermission
                {
                    FileDocumentId_PK = docNew.ID,
                    DepartmentId_PK = 0,
                    IsFolder = docNew.IsFolder,
                    Rename = true,
                    Move = true,
                    Copy = true,
                    Delete = true,
                    Download = true,
                    Create = true,
                    Upload = true,
                    MoveOrCopyInto = true,
                };

                var permissionsOnlyView = new FileDocumentPermissionsOnlyView
                {
                    FileDocumentId_PK = docNew.ID,
                    DepartmentId_PK = 0,
                    IsFolder = docNew.IsFolder,
                    Rename = true,
                    Move = true,
                    Copy = true,
                    Delete = true,
                    Download = true,
                    Create = true,
                    Upload = true,
                    MoveOrCopyInto = true,
                };

                _fileDocumentPermissions.AddPermissions(permissions);
                _fileDocumentPermissionsOnlyView.AddPermissions(permissionsOnlyView);
                return true;
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
                return false;
            }
        }

        public bool CustomPermissions(Department item, FileDocument docNew)
        {
            try
            {
                string[] PathLuatvaKSNB = null;
                if (SSIConfig.PathLuatvaKSNB.Contains('|'))
                    PathLuatvaKSNB = SSIConfig.PathLuatvaKSNB.Split('|').Select(s => s).ToArray();
                else PathLuatvaKSNB = new[] { SSIConfig.PathLuatvaKSNB };

                string[] PathHanhChinh = null;
                if (SSIConfig.PathHanhChinh.Contains('|'))
                    PathHanhChinh = SSIConfig.PathHanhChinh.Split('|').Select(s => s).ToArray();
                else PathHanhChinh = new[] { SSIConfig.PathHanhChinh };

                string[] PathDVCK = null;
                if (SSIConfig.PathDVCK.Contains('|'))
                    PathDVCK = SSIConfig.PathDVCK.Split('|').Select(s => s).ToArray();
                else PathDVCK = new[] { SSIConfig.PathDVCK };

                string[] PathPublic = null;
                if (SSIConfig.PathPublic.Contains('|'))
                    PathPublic = SSIConfig.PathPublic.Split('|').Select(s => s).ToArray();
                else PathPublic = new[] { SSIConfig.PathPublic };

                //trong pathPublic - không pải phòng ban upload -> không cho download
                var c_download = PathPublic.Contains(docNew.Path.Substring(0, 4)) ? false : true;
                //Nếu là pathPublic thì ko có upload ở folder root
                var c_upload = PathPublic.Contains(docNew.Path) ? false : true;

                //Nếu là tài khoản chỉ có quyền View
                var permissionsOnlyView = new FileDocumentPermissionsOnlyView
                {
                    FileDocumentId_PK = docNew.ID,
                    DepartmentId_PK = item.ID,
                    IsFolder = docNew.IsFolder,
                    Rename = false,
                    Move = false,
                    Copy = false,
                    Delete = false,
                    Download = false,
                    Create = false,
                    Upload = false,
                    MoveOrCopyInto = false
                };

                //Custom follow rules
                FileDocumentPermission permissions = new FileDocumentPermission();

                //Phòng LuatvaKSNB
                if (item.ID == (int)Enums.SpecialDepartments.LuatvaKSNB)
                {
                    //upload vào các folder mà LuatvaKSNB là admin
                    //Nếu không phải admin của folder thì xem user upload thuộc phòng LuatvaKSNB hay không
                    if (PathLuatvaKSNB.Contains(docNew.Path.Substring(0, 4)) || item.ID == _currentUser.DepartmentId)
                    {
                        //Add full quyền cho LuatvaKSNB
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = true,
                            Move = true,
                            Copy = true,
                            Delete = true,
                            Download = true,
                            Create = true,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                    else
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = false,
                            Move = false,
                            Copy = false,
                            Delete = false,
                            Download = c_download,
                            Create = false,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                }
                else if (item.ID == (int)Enums.SpecialDepartments.HanhChinh)
                {
                    if (PathHanhChinh.Contains(docNew.Path.Substring(0, 4)) || item.ID == _currentUser.DepartmentId)
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = true,
                            Move = true,
                            Copy = true,
                            Delete = true,
                            Download = true,
                            Create = true,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                    else
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = false,
                            Move = false,
                            Copy = false,
                            Delete = false,
                            Download = c_download,
                            Create = false,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                }
                else if (item.ID == (int)Enums.SpecialDepartments.DichVuChungKhoan)
                {
                    if (PathDVCK.Contains(docNew.Path.Substring(0, 4)) || item.ID == _currentUser.DepartmentId)
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = true,
                            Move = true,
                            Copy = true,
                            Delete = true,
                            Download = true,
                            Create = true,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                    else
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = false,
                            Move = false,
                            Copy = false,
                            Delete = false,
                            Download = c_download,
                            Create = false,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                }
                else
                {
                    //nếu tạo ở thu muc public và cung phòng ban -> full quyen
                    if (item.ID == _currentUser.DepartmentId)
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = true,
                            Move = true,
                            Copy = true,
                            Delete = true,
                            Download = true,
                            Create = true,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                    else
                    {
                        permissions = new FileDocumentPermission
                        {
                            FileDocumentId_PK = docNew.ID,
                            DepartmentId_PK = item.ID,
                            IsFolder = docNew.IsFolder,
                            Rename = false,
                            Move = false,
                            Copy = false,
                            Delete = false,
                            Download = c_download,
                            Create = false,
                            Upload = c_upload,
                            MoveOrCopyInto = true
                        };
                    }
                }

                _fileDocumentPermissionsOnlyView.AddPermissions(permissionsOnlyView);
                _fileDocumentPermissions.AddPermissions(permissions);
                return true;
                //return db.SaveChanges() > 0 ? true : false;
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
                return false;
            }
        }

        #endregion Custom Permissions

        #endregion custom-filesystem-provider-and-set-file-folder-permissions

        #endregion Access Rules

        #region Rename

        public override void RenameFile(FileManagerFile file, string name)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException("Tên file không hợp lệ");
            }

            var docId = Convert.ToInt32(file.Id);
            var doc = _fileDataService.Get(x => x.ID == docId);

            if (doc == null) throw new FileNotFoundException("File không còn tồn tại");

            //string err = string.Empty;
            //var canModify = _filePermissionManager.HasPermission(_currentUser, doc, PermissionCode.Modify, out err);

            //if (!canModify)
            //{
            //    throw new UnauthorizedAccessException(err);
            //}
            var mimeTypeOld = MimeMapping.GetMimeMapping(doc.Name);
            var extensionOld = Path.GetExtension(doc.Name);

            var mimeTypeNew = MimeMapping.GetMimeMapping(name);
            var extensionNew = Path.GetExtension(name);
            if (mimeTypeOld != mimeTypeNew)
            {
                if (mimeTypeNew == "application/octet-stream")
                    name = name + extensionOld;
                else
                    name = name.Replace(extensionNew, extensionOld);
            }
            doc.Name = name;
            doc.UpdatedByUser = _currentUser.UserName;
            doc.UpdatedDate = DateTime.Now;
            doc.LastWriteTime = DateTime.Now;

            _fileDataService.Update(doc);
            _fileDataService.SaveChanges();
            RefreshCache();
        }

        public override void RenameFolder(FileManagerFolder folder, string name)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException("Tên folder không hợp lệ");
            }

            var doc = FindDocFolderItem(folder);

            if (doc == null) throw new FileNotFoundException("Thư mục không còn tồn tại");

            doc.Item.Name = name;
            doc.Item.UpdatedByUser = _currentUser.UserName;
            doc.Item.UpdatedDate = DateTime.Now;
            doc.Item.LastWriteTime = DateTime.Now;

            _fileDataService.Update(doc.Item);
            _fileDataService.SaveChanges();
            RefreshCache();
        }

        #endregion Rename

        #region Move

        public override void MoveFile(FileManagerFile file, FileManagerFolder newParentFolder)
        {
            var docId = Convert.ToInt32(file.Id);
            var doc = _fileDataService.Get(x => x.ID == docId);
            if (doc == null) return;

            string err = string.Empty;
            //var canDelete = _filePermissionManager.HasPermission(_currentUser, doc, PermissionCode.Delete, out err);
            //if (!canDelete)
            //{
            //    throw new UnauthorizedAccessException(err);
            //}

            var folder = FindDocFolderItem(newParentFolder);
            if (folder == null) throw new UnauthorizedAccessException("Không tìm thấy đường dẫn cần chuyển tới.");

            //var canCreate = _filePermissionManager.HasPermission(_currentUser, doc, PermissionCode.Write, out err);
            //if (!canCreate)
            //{
            //    throw new UnauthorizedAccessException(err);
            //}

            using (var transaction = _fileDataService.BeginTransaction())
            {
                try
                {
                    doc.ParentID = folder.Item.ID;
                    doc.UpdatedByUser = _currentUser.UserName;
                    doc.UpdatedDate = DateTime.Now;
                    doc.LastWriteTime = DateTime.Now;

                    _fileDataService.Update(doc);
                    _fileDataService.ReloadNodePath(doc);
                    _fileDataService.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    _log.FatalFormat("{0}: has exception {1}", MethodBase.GetCurrentMethod().Name, ex);
                    transaction.RollBack();
                    throw;
                }
            }
            RefreshCache();
        }

        public override void MoveFolder(FileManagerFolder folder, FileManagerFolder newParentFolder)
        {
            var currentFolder = FindDocFolderItem(folder);
            if (currentFolder == null) return;

            //string err = string.Empty;
            //var canDelete = _filePermissionManager.HasPermission(_currentUser, currentFolder, PermissionCode.Delete, out err);
            //if (!canDelete)
            //{
            //    throw new UnauthorizedAccessException(err);
            //}

            var parentFolder = FindDocFolderItem(newParentFolder);
            if (parentFolder == null) throw new UnauthorizedAccessException("Không tìm thấy đường dẫn cần chuyển tới.");

            //var canCreate = _filePermissionManager.HasPermission(_currentUser, parentFolder, PermissionCode.Write, out err);
            //if (!canCreate)
            //{
            //    throw new UnauthorizedAccessException(err);
            //}

            using (var transaction = _fileDataService.BeginTransaction())
            {
                try
                {
                    currentFolder.Item.ParentID = parentFolder.Item.ID;
                    currentFolder.Item.UpdatedByUser = _currentUser.UserName;
                    currentFolder.Item.UpdatedDate = DateTime.Now;
                    currentFolder.Item.LastWriteTime = DateTime.Now;

                    _fileDataService.Update(currentFolder.Item);
                    _fileDataService.ReloadNodePath(currentFolder.Item);
                    _fileDataService.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.RollBack();
                    throw;
                }
            }
            RefreshCache();
        }

        #endregion Move

        #region Delete

        public override void DeleteFile(FileManagerFile file)
        {
            var docId = Convert.ToInt32(file.Id);
            var doc = _fileDataService.Get(x => x.ID == docId);

            if (doc == null) throw new FileNotFoundException("File không tồn tại");

            //File replated...
            string err = string.Empty;
            var canDelete = _filePermissionManager.HasPermissionByDoc(_currentUser, doc, Enums.PermissionCode.Delete, out err);
            if (!canDelete)
            {
                throw new UnauthorizedAccessException(err);
            }

            doc.UpdatedByUser = _currentUser.UserName;
            doc.UpdatedDate = DateTime.Now;
            doc.LastWriteTime = DateTime.Now;

            _fileDataService.DeleteNode(doc);
            _fileDataService.SaveChanges();
        }

        public override void DeleteFolder(FileManagerFolder folder)
        {
            var doc = FindDocFolderItem(folder);

            if (doc == null) throw new FileNotFoundException("Folder không tồn tại");

            //File replated...
            string err = string.Empty;
            var canDelete = _filePermissionManager.HasPermissionByDoc(_currentUser, doc.Item, Enums.PermissionCode.Delete, out err);
            if (!canDelete)
            {
                throw new UnauthorizedAccessException(err);
            }

            doc.Item.UpdatedByUser = _currentUser.UserName;
            doc.Item.UpdatedDate = DateTime.Now;
            doc.Item.LastWriteTime = DateTime.Now;

            _fileDataService.DeleteNode(doc.Item);
            _fileDataService.SaveChanges();
        }

        #endregion Delete

        public override bool Exists(FileManagerFile file)
        {
            var docId = Convert.ToInt32(file.Id);
            return _fileDataService.Any(x => x.ID == docId);
        }

        public override bool Exists(FileManagerFolder folder)
        {
            return FindDocFolderItem(folder) != null;
        }

        public override DateTime GetLastWriteTime(FileManagerFile file)
        {
            var docId = Convert.ToInt32(file.Id);
            //Chỉ lấy trường cần thiết
            var lastWriteTime = _fileDataService.GetAll(x => x.ID == docId).Select(x => x.LastWriteTime).FirstOrDefault();
            return lastWriteTime.GetValueOrDefault();
        }

        public override DateTime GetLastWriteTime(FileManagerFolder folder)
        {
            var doc = FindDocFolderItem(folder);
            return doc.Item.LastWriteTime.GetValueOrDefault();
        }

        public override long GetLength(FileManagerFile file)
        {
            //Chỉ lấy trường cần thiết
            var docId = Convert.ToInt32(file.Id);
            var length = _fileDataService.GetAll(x => x.ID == docId).Select(x => x.FileSize).FirstOrDefault();
            return length;
        }

        #region Func helper

        public FileDocument FindFolderByFullname(string fullName)
        {
            try
            {
                FileDocument result = new FileDocument();
                foreach (var i in _folderCache.Values)
                {
                    string fName = GetFullName(i.Item);
                    if (fName == fullName)
                        result = i.Item;
                }
                return result;
                //return _folderCache.Values.Where(x => GetFullName(x) == fullName).FirstOrDefault();
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
                return new FileDocument();
            }
        }

        public string GetFullName(FileDocument doc)
        {
            try
            {
                if (doc.ParentID < 0)
                {
                    return doc.Name;
                }

                var parentDoc = _folderCache.Values.FirstOrDefault(x => x.Item.ID == doc.ParentID);

                return GetFullName(parentDoc.Item) + "\\" + doc.Name;
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
                return "";
            }
        }

        protected FileViewModel FindDocViewModel(FileManagerFile file)
        {
            try
            {
                //FileDocument docFolder = FindDocFolderItem(file.Folder);
                //if (docFolder == null)
                //    return null;
                //return _fileDataService.Get(item => (int)item.ParentID == docFolder.ID && !item.IsFolder && !item.IsDeleted && item.Name == file.Name);
                var docId = Convert.ToInt32(file.Id);

                //Chỉ lấy trường cần thiết
                var result = _fileDataService.GetAll(x => !x.IsDeleted && x.ID == docId).Select(x => new FileViewModel
                {
                    ID = x.ID,
                    Name = x.Name,
                    FileNo = x.FileNo,
                    //ReplacedByID = x.ReplacedByID,
                    //ReplaceID = x.ReplaceID,
                    IsPublic = x.IsPublic,
                    Department = x.Department,
                    Path = x.Path,
                    DocumentTypeCategory = x.DocumentTypeCategory,
                    DocumentType = x.DocumentType,
                    AgencyType = x.AgencyType,
                    EffectiveFrom = x.EffectiveFrom,
                    EffectiveTo = x.EffectiveTo,
                }).FirstOrDefault();

                return result;
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
                return new FileViewModel();
            }
        }

        protected CacheEntry FindDocFolderItem(FileManagerFolder folder)
        {
            var model = (from i in _folderCache.Values
                         where i.Item.IsFolder && !i.Item.IsDeleted && GetRelativeName(i.Item) == folder.RelativeName
                         select i).FirstOrDefault();
            return model;
        }

        protected string GetRelativeName(FileDocument doc)
        {
            try
            {
                if (doc.ID == ROOT_ITEM_ID) return string.Empty;

                if (doc.ParentID == ROOT_ITEM_ID) return doc.Name;

                if (!_folderCache.ContainsKey((int)doc.ParentID)) return null;

                var parent = _folderCache.Values.Where(x => x.Item.ID == doc.ParentID).FirstOrDefault();
                string name = GetRelativeName(parent.Item);

                return name == null ? null : Path.Combine(name, doc.Name);
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
                return "";
            }
        }

        #endregion Func helper

        private void RefreshCache()
        {
            string err = string.Empty;
            DMSEntities dbEntities = new DMSEntities();
            try
            {
                var _folderCachePrepare = _fileDataService.GetAll(x => x.IsFolder && !x.IsDeleted).ToList();

                _folderCache = _folderCachePrepare
                .Join(dbEntities.FileDocumentPermissions.ToList()
                .Where(m => m.IsFolder == true && m.DepartmentId_PK == _currentUser.DepartmentId), i => i.ID, p => p.FileDocumentId_PK, (i, pi)
                => new
                {
                    Item = i,
                    Permissions = pi
                })
                .ToDictionary(dto => dto.Item.ID, dto => new CacheEntry
                {
                    Item = dto.Item,
                    Permissions = dto.Permissions
                });

                if (_currentUser.UserType == (short)Enums.UserType.OnlyView)
                {
                    var _folderCacheTemp = _folderCachePrepare
                    .Join(dbEntities.FileDocumentPermissionsOnlyViews.ToList()
                    .Where(m => m.IsFolder == true && m.DepartmentId_PK == _currentUser.DepartmentId), i => i.ID, p => p.FileDocumentId_PK, (i, pi)
                    => new
                    {
                        Item = i,
                        Permissions = pi
                    }).ToDictionary(dto => dto.Item.ID, dto => new CacheEntryOnlyView
                    {
                        Item = dto.Item,
                        Permissions = dto.Permissions
                    });

                    foreach (var i in _folderCacheTemp)
                    {
                        if (_folderCache.Count == _folderCacheTemp.Count && _folderCache.Keys.SequenceEqual(_folderCacheTemp.Keys))
                        {
                            _folderCache[i.Key].Permissions.Id = _folderCacheTemp[i.Key].Permissions.Id;
                            _folderCache[i.Key].Permissions.FileDocumentId_PK = _folderCacheTemp[i.Key].Permissions.FileDocumentId_PK;
                            _folderCache[i.Key].Permissions.DepartmentId_PK = _folderCacheTemp[i.Key].Permissions.DepartmentId_PK;
                            _folderCache[i.Key].Permissions.Rename = _folderCacheTemp[i.Key].Permissions.Rename;
                            _folderCache[i.Key].Permissions.Move = _folderCacheTemp[i.Key].Permissions.MoveOrCopyInto;
                            _folderCache[i.Key].Permissions.Copy = _folderCacheTemp[i.Key].Permissions.Copy;
                            _folderCache[i.Key].Permissions.Delete = _folderCacheTemp[i.Key].Permissions.Delete;
                            _folderCache[i.Key].Permissions.Download = _folderCacheTemp[i.Key].Permissions.Download;
                            _folderCache[i.Key].Permissions.Create = _folderCacheTemp[i.Key].Permissions.Create;
                            _folderCache[i.Key].Permissions.Upload = _folderCacheTemp[i.Key].Permissions.Upload;
                            _folderCache[i.Key].Permissions.MoveOrCopyInto = _folderCacheTemp[i.Key].Permissions.MoveOrCopyInto;
                            _folderCache[i.Key].Permissions.IsFolder = _folderCacheTemp[i.Key].Permissions.IsFolder;
                        }
                        else
                        {
                            _log.FatalFormat("{0} : has _folderCache and _folderCacheTemp don't sync", MethodBase.GetCurrentMethod());
                            throw new Exception("Danh sách quyền không đồng bộ. Vui lòng liên hệ admin: lammn@ssi.com.vn");
                        }
                    }
                }
                var result = _folderCache.ToList();

                //var _folderCacheDictionary = _fileDataService.GetAll(x => x.IsFolder && !x.IsDeleted).ToDictionary(x => x.ID).ToList();
                //_folderCache = _fileDataService.GetAll(x => x.IsFolder && !x.IsDeleted).ToDictionary(x => x.ID);
            }
            catch (DbEntityValidationException e)
            {
                foreach (var eve in e.EntityValidationErrors)
                {
                    Console.WriteLine("Entity of type \"{0}\" in state \"{1}\" has the following validation errors:",
                        eve.Entry.Entity.GetType().Name, eve.Entry.State);
                    foreach (var ve in eve.ValidationErrors)
                    {
                        Console.WriteLine("- Property: \"{0}\", Error: \"{1}\"",
                            ve.PropertyName, ve.ErrorMessage);
                        err += "- Property: " + ve.PropertyName + ", Error: " + ve.ErrorMessage + ", ";
                    }
                }
                err += e.Message;
                _log.FatalFormat("{0} : has DbEntityValidationException {1}", MethodBase.GetCurrentMethod(), err);
            }
            catch (Exception ex)
            {
                _log.FatalFormat("{0} : has exception {1}", MethodBase.GetCurrentMethod(), ex);
            }
        }

        public class CacheEntry
        {
            public FileDocument Item { get; set; }
            public FileDocumentPermission Permissions { get; set; }
        }

        public class CacheEntryOnlyView
        {
            public FileDocument Item { get; set; }
            public FileDocumentPermissionsOnlyView Permissions { get; set; }
        }
    }
}