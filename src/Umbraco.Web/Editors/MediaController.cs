﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.ModelBinding;
using AutoMapper;
using Umbraco.Core;
using Umbraco.Core.Dynamics;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Media;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Editors;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.Services;
using Umbraco.Web.Models;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Web.Models.Mapping;
using Umbraco.Web.Mvc;
using Umbraco.Web.WebApi;
using System.Linq;
using Umbraco.Web.WebApi.Binders;
using Umbraco.Web.WebApi.Filters;
using umbraco;
using umbraco.BusinessLogic.Actions;
using Constants = Umbraco.Core.Constants;
using Umbraco.Core.Configuration;

namespace Umbraco.Web.Editors
{


    /// <remarks>
    /// This controller is decorated with the UmbracoApplicationAuthorizeAttribute which means that any user requesting
    /// access to ALL of the methods on this controller will need access to the media application.
    /// </remarks>
    [PluginController("UmbracoApi")]
    [UmbracoApplicationAuthorizeAttribute(Constants.Applications.Media)]
    public class MediaController : ContentControllerBase
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public MediaController()
            : this(UmbracoContext.Current)
        {            
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="umbracoContext"></param>
        public MediaController(UmbracoContext umbracoContext)
            : base(umbracoContext)
        {
        }

        /// <summary>
        /// Gets the big thumbnail image for the media id
        /// </summary>
        /// <param name="mediaId"></param>
        /// <returns></returns>
        /// <remarks>
        /// If there is no media, image property or image file is found then this will return not found.
        /// </remarks>
        public HttpResponseMessage GetBigThumbnail(int mediaId)
        {
            var media = Services.MediaService.GetById(mediaId);
            if (media == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var imageProp = media.Properties[Constants.Conventions.Media.File];
            if (imageProp == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var imagePath = imageProp.Value.ToString();
            return GetBigThumbnail(imagePath);
        }

        /// <summary>
        /// Gets the big thumbnail image for the original image path
        /// </summary>
        /// <param name="originalImagePath"></param>
        /// <returns></returns>
        /// <remarks>
        /// If there is no original image is found then this will return not found.
        /// </remarks>
        public HttpResponseMessage GetBigThumbnail(string originalImagePath)
        {
            var imagePath = originalImagePath;
            var bigThumbPath = imagePath.Substring(0, imagePath.LastIndexOf('.')) + "_big-thumb" + ".jpg";
            var thumbFilePath = IOHelper.MapPath(bigThumbPath);
            if (System.IO.File.Exists(thumbFilePath) == false)
            {
                //we need to generate it
                var origFilePath = IOHelper.MapPath(imagePath);
                if (System.IO.File.Exists(origFilePath) == false)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                var mediaFileSystem = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();
                using (var fileStream = new FileStream(origFilePath, FileMode.Open, FileAccess.Read))
                {
                    if (fileStream.CanSeek) fileStream.Seek(0, 0);
                    using (var originalImage = Image.FromStream(fileStream))
                    {
                        ImageHelper.GenerateThumbnail(
                            originalImage,
                            500,
                            string.Format("{0}_{1}.jpg", origFilePath.Substring(0, origFilePath.LastIndexOf(".")), "big-thumb"),
                            Path.GetExtension(origFilePath).Substring(1).ToLowerInvariant(),
                            mediaFileSystem);
                    }
                }
            }

            var result = new HttpResponseMessage(HttpStatusCode.OK);
            //NOTE: That we are not closing this stream as the framework will do that for us, if we try it will
            // fail. See http://stackoverflow.com/questions/9541351/returning-binary-file-from-controller-in-asp-net-web-api
            var stream = new FileStream(thumbFilePath, FileMode.Open, FileAccess.Read);
            result.Content = new StreamContent(stream);
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return result;
        }


        /// <summary>
        /// Gets an empty content item for the 
        /// </summary>
        /// <param name="contentTypeAlias"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>
        public MediaItemDisplay GetEmpty(string contentTypeAlias, int parentId)
        {
            var contentType = Services.ContentTypeService.GetMediaType(contentTypeAlias);
            if (contentType == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var emptyContent = new Core.Models.Media("", parentId, contentType);
            var mapped = Mapper.Map<IMedia, MediaItemDisplay>(emptyContent);

            //remove this tab if it exists: umbContainerView
            var containerTab = mapped.Tabs.FirstOrDefault(x => x.Alias == "umbContainerView");
            mapped.Tabs = mapped.Tabs.Except(new[] { containerTab });
            return mapped;
        }

        /// <summary>
        /// Gets the content json for the content id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [EnsureUserPermissionForMedia("id")]
        public MediaItemDisplay GetById(int id)
        {
            var foundContent = GetObjectFromRequest(() => Services.MediaService.GetById(id));

            if (foundContent == null)
            {
                HandleContentNotFound(id);
            }
            return Mapper.Map<IMedia, MediaItemDisplay>(foundContent);
        }

        /// <summary>
        /// Return media for the specified ids
        /// </summary>
        /// <param name="ids"></param>
        /// <returns></returns>
        [FilterAllowedOutgoingMedia(typeof(IEnumerable<MediaItemDisplay>))]
        public IEnumerable<MediaItemDisplay> GetByIds([FromUri]int[] ids)
        {
            var foundMedia = Services.MediaService.GetByIds(ids);
            return foundMedia.Select(Mapper.Map<IMedia, MediaItemDisplay>);
        }


        /// <summary>
        /// Returns the root media objects
        /// </summary>
        [FilterAllowedOutgoingMedia(typeof(IEnumerable<ContentItemBasic<ContentPropertyBasic, IMedia>>))]
        public IEnumerable<ContentItemBasic<ContentPropertyBasic, IMedia>> GetRootMedia()
        {
            //TODO: Add permissions check!

            return Services.MediaService.GetRootMedia()
                           .Select(Mapper.Map<IMedia, ContentItemBasic<ContentPropertyBasic, IMedia>>);
        }

        /// <summary>
        /// Returns the child media objects
        /// </summary>
        [FilterAllowedOutgoingMedia(typeof(IEnumerable<ContentItemBasic<ContentPropertyBasic, IMedia>>), "Items")]
        public PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>> GetChildren(int id,
            int pageNumber = 0,
            int pageSize = 0,
            string orderBy = "SortOrder",
            Direction orderDirection = Direction.Ascending,
            string filter = "")
        {
            //TODO: This will be horribly inefficient for paging! This is because our datasource/repository 
            // doesn't support paging at the SQL level... and it'll be pretty interesting to try to make that work.

            var children = Services.MediaService.GetChildren(id).ToArray();
            var totalChildren = children.Length;

            if (totalChildren == 0)
                return new PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>>(0, 0, 0);

            var result = children
                .Select(Mapper.Map<IMedia, ContentItemBasic<ContentPropertyBasic, IMedia>>)
                .AsQueryable();

            //TODO: This is a rudimentry filter - should use the logic found in the EntityService filter (dynamic linq) instead
            if (!string.IsNullOrEmpty(filter))
            {
                filter = filter.ToLower();
                result = result.Where(x => x.Name.InvariantContains(filter));
            }

            var orderedResult = orderDirection == Direction.Ascending
                ? result.OrderBy(orderBy)
                : result.OrderByDescending(orderBy);

            var pagedResult = new PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>>(
               totalChildren,
               pageNumber,
               pageSize);

            if (pageNumber > 0 && pageSize > 0)
            {
                pagedResult.Items = orderedResult
                    .Skip(pagedResult.SkipSize)
                    .Take(pageSize);
            }
            else
            {
                pagedResult.Items = orderedResult;
            }

            return pagedResult;

        }

        /// <summary>
        /// Moves an item to the recycle bin, if it is already there then it will permanently delete it
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns> 
        [EnsureUserPermissionForMedia("id")]
        public HttpResponseMessage DeleteById(int id)
        {
            var foundMedia = GetObjectFromRequest(() => Services.MediaService.GetById(id));

            if (foundMedia == null)
            {
                return HandleContentNotFound(id, false);
            }

            //if the current item is in the recycle bin
            if (foundMedia.IsInRecycleBin() == false)
            {
                Services.MediaService.MoveToRecycleBin(foundMedia, (int)Security.CurrentUser.Id);
            }
            else
            {
                Services.MediaService.Delete(foundMedia, (int)Security.CurrentUser.Id);
            }

            return Request.CreateResponse(HttpStatusCode.OK);
        }

        /// <summary>
        /// Change the sort order for media
        /// </summary>
        /// <param name="move"></param>
        /// <returns></returns>
        [EnsureUserPermissionForMedia("move.Id")]
        public HttpResponseMessage PostMove(MoveOrCopy move)
        {
            var toMove = ValidateMoveOrCopy(move);

            Services.MediaService.Move(toMove, move.ParentId);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(toMove.Path, Encoding.UTF8, "application/json")
            };
            return response; 
        }

        /// <summary>
        /// Saves content
        /// </summary>
        /// <returns></returns>        
        [FileUploadCleanupFilter]
        [MediaPostValidate]
        public MediaItemDisplay PostSave(
            [ModelBinder(typeof(MediaItemBinder))]
                MediaItemSave contentItem)
        {
            //If we've reached here it means:
            // * Our model has been bound
            // * and validated
            // * any file attachments have been saved to their temporary location for us to use
            // * we have a reference to the DTO object and the persisted object
            // * Permissions are valid

            MapPropertyValues(contentItem);

            //We need to manually check the validation results here because:
            // * We still need to save the entity even if there are validation value errors
            // * Depending on if the entity is new, and if there are non property validation errors (i.e. the name is null)
            //      then we cannot continue saving, we can only display errors
            // * If there are validation errors and they were attempting to publish, we can only save, NOT publish and display 
            //      a message indicating this
            if (!ModelState.IsValid)
            {
                if (ValidationHelper.ModelHasRequiredForPersistenceErrors(contentItem)
                    && (contentItem.Action == ContentSaveAction.SaveNew))
                {
                    //ok, so the absolute mandatory data is invalid and it's new, we cannot actually continue!
                    // add the modelstate to the outgoing object and throw validation response
                    var forDisplay = Mapper.Map<IMedia, MediaItemDisplay>(contentItem.PersistedContent);
                    forDisplay.Errors = ModelState.ToErrorDictionary();
                    throw new HttpResponseException(Request.CreateValidationErrorResponse(forDisplay));
                }
            }

            //save the item
            Services.MediaService.Save(contentItem.PersistedContent, (int)Security.CurrentUser.Id);

            //return the updated model
            var display = Mapper.Map<IMedia, MediaItemDisplay>(contentItem.PersistedContent);
            
            //lasty, if it is not valid, add the modelstate to the outgoing object and throw a 403
            HandleInvalidModelState(display);

            //put the correct msgs in 
            switch (contentItem.Action)
            {
                case ContentSaveAction.Save:
                case ContentSaveAction.SaveNew:
                    display.AddSuccessNotification(ui.Text("speechBubbles", "editMediaSaved"), ui.Text("speechBubbles", "editMediaSavedText"));
                    break;                
            }

            return display;
        }

        /// <summary>
        /// Maps the property values to the persisted entity
        /// </summary>
        /// <param name="contentItem"></param>
        protected override void MapPropertyValues<TPersisted>(ContentBaseItemSave<TPersisted> contentItem)
        {
            UpdateName(contentItem);

            //use the base method to map the rest of the properties
            base.MapPropertyValues(contentItem);
        }

        /// <summary>
        /// Empties the recycle bin
        /// </summary>
        /// <returns></returns>
        [HttpDelete]
        public HttpResponseMessage EmptyRecycleBin()
        {
            Services.MediaService.EmptyRecycleBin();
            return Request.CreateResponse(HttpStatusCode.OK);
        }



        
        /// <summary>
        /// Change the sort order for media
        /// </summary>
        /// <param name="sorted"></param>
        /// <returns></returns>
        [EnsureUserPermissionForMedia("sorted.ParentId")]
        public HttpResponseMessage PostSort(ContentSortOrder sorted)
        {
            if (sorted == null)
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }

            //if there's nothing to sort just return ok
            if (sorted.IdSortOrder.Length == 0)
            {
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            
            var mediaService = base.ApplicationContext.Services.MediaService;
            var sortedMedia = new List<IMedia>();
            try
            {
                sortedMedia.AddRange(sorted.IdSortOrder.Select(mediaService.GetById));

                // Save Media with new sort order and update content xml in db accordingly
                if (mediaService.Sort(sortedMedia) == false)
                {
                    LogHelper.Warn<MediaController>("Media sorting failed, this was probably caused by an event being cancelled");
                    return Request.CreateValidationErrorResponse("Media sorting failed, this was probably caused by an event being cancelled");
                }
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                LogHelper.Error<MediaController>("Could not update media sort order", ex);
                throw;
            }
        }

        [EnsureUserPermissionForMedia("folder.ParentId")]
        public MediaItemDisplay PostAddFolder(EntityBasic folder)
        {
            var mediaService = ApplicationContext.Services.MediaService;
            var f = mediaService.CreateMedia(folder.Name, folder.ParentId, Constants.Conventions.MediaTypes.Folder);
            mediaService.Save(f);

            return Mapper.Map<IMedia, MediaItemDisplay>(f);
        }

        /// <summary>
        /// Used to submit a media file
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// We cannot validate this request with attributes (nicely) due to the nature of the multi-part for data.
        /// 
        /// TOOD: Validate this request properly!
        /// </remarks>
        public async Task<HttpResponseMessage> PostAddFile()
        {
            if (Request.Content.IsMimeMultipartContent() == false)
            {
                throw new HttpResponseException(HttpStatusCode.UnsupportedMediaType);
            }

            var root = IOHelper.MapPath("~/App_Data/TEMP/FileUploads");
            //ensure it exists
            Directory.CreateDirectory(root);
            var provider = new MultipartFormDataStreamProvider(root);

            var result = await Request.Content.ReadAsMultipartAsync(provider);
            
            //must have a file
            if (result.FileData.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            //get the string json from the request
            int parentId;
            if (int.TryParse(result.FormData["currentFolder"], out parentId) == false)
            {
                throw new HttpResponseException(
                    new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        ReasonPhrase = "The request was not formatted correctly, the currentFolder is not an integer"
                    });
            }

            //ensure the user has access to this folder by parent id!
            if (CheckPermissions(
               new Dictionary<string, object>(), 
               Security.CurrentUser,
               Services.MediaService, parentId) == false)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            //get the files
            foreach (var file in result.FileData)
            {
                var fileName = file.Headers.ContentDisposition.FileName.Trim(new[] { '\"' });
                var ext = fileName.Substring(fileName.LastIndexOf('.')+1).ToLower();

                if (!UmbracoConfig.For.UmbracoSettings().Content.DisallowedUploadFiles.Contains(ext))
                {
                    var mediaType = Constants.Conventions.MediaTypes.File;

                    if (UmbracoConfig.For.UmbracoSettings().Content.ImageFileTypes.Contains(ext))
                        mediaType = Constants.Conventions.MediaTypes.Image;

                    var mediaService = ApplicationContext.Services.MediaService;
                    var f = mediaService.CreateMedia(fileName, parentId, mediaType);
                    using (var fs = System.IO.File.OpenRead(file.LocalFileName))
                    {
                        f.SetValue(Constants.Conventions.Media.File, fileName, fs);
                    }

                    mediaService.Save(f);
                }

                //now we can remove the temp file
                System.IO.File.Delete(file.LocalFileName);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }


        /// <summary>
        /// Ensures the item can be moved/copied to the new location
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        private IMedia ValidateMoveOrCopy(MoveOrCopy model)
        {
            if (model == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var mediaService = Services.MediaService;
            var toMove = mediaService.GetById(model.Id);
            if (toMove == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
            if (model.ParentId < 0)
            {
                //cannot move if the content item is not allowed at the root
                if (toMove.ContentType.AllowedAsRoot == false)
                {
                    throw new HttpResponseException(
                        Request.CreateValidationErrorResponse(ui.Text("moveOrCopy", "notAllowedAtRoot", Security.CurrentUser)));
                }
            }
            else
            {
                var parent = mediaService.GetById(model.ParentId);
                if (parent == null)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                //check if the item is allowed under this one
                if (parent.ContentType.AllowedContentTypes.Select(x => x.Id).ToArray()
                    .Any(x => x.Value == toMove.ContentType.Id) == false)
                {
                    throw new HttpResponseException(
                        Request.CreateValidationErrorResponse(ui.Text("moveOrCopy", "notAllowedByContentType", Security.CurrentUser)));
                }

                // Check on paths
                if ((string.Format(",{0},", parent.Path)).IndexOf(string.Format(",{0},", toMove.Id), StringComparison.Ordinal) > -1)
                {
                    throw new HttpResponseException(
                        Request.CreateValidationErrorResponse(ui.Text("moveOrCopy", "notAllowedByPath", Security.CurrentUser)));
                }
            }

            return toMove;
        }

        /// <summary>
        /// Performs a permissions check for the user to check if it has access to the node based on 
        /// start node and/or permissions for the node
        /// </summary>
        /// <param name="storage">The storage to add the content item to so it can be reused</param>
        /// <param name="user"></param>
        /// <param name="mediaService"></param>
        /// <param name="nodeId">The content to lookup, if the contentItem is not specified</param>
        /// <param name="media">Specifies the already resolved content item to check against, setting this ignores the nodeId</param>
        /// <returns></returns>
        internal static bool CheckPermissions(IDictionary<string, object> storage, IUser user, IMediaService mediaService, int nodeId, IMedia media = null)
        {
            if (media == null && nodeId != Constants.System.Root && nodeId != Constants.System.RecycleBinMedia)
            {
                media = mediaService.GetById(nodeId);
                //put the content item into storage so it can be retreived 
                // in the controller (saves a lookup)
                storage[typeof(IMedia).ToString()] = media;
            }

            if (media == null && nodeId != Constants.System.Root && nodeId != Constants.System.RecycleBinMedia)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var hasPathAccess = (nodeId == Constants.System.Root)
                                    ? UserExtensions.HasPathAccess(
                                        Constants.System.Root.ToInvariantString(),
                                        user.StartMediaId,
                                        Constants.System.RecycleBinMedia)
                                    : (nodeId == Constants.System.RecycleBinMedia)
                                          ? UserExtensions.HasPathAccess(
                                              Constants.System.RecycleBinMedia.ToInvariantString(),
                                              user.StartMediaId,
                                              Constants.System.RecycleBinMedia)
                                          : user.HasPathAccess(media);

            return hasPathAccess;
        }
    }
}
