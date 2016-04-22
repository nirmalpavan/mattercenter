﻿// ***********************************************************************
// Assembly         : Microsoft.Legal.MatterCenter.Utility
// Author           : v-lapedd
// Created          : 04-07-2016
//
// ***********************************************************************
// <copyright file="MatterRepository.cs" company="Microsoft">
//     Copyright (c) . All rights reserved.
// </copyright>
// This class deals with all the matter related functions such as finding matter, pin, unpin, update matter etc
// ***********************************************************************

using Microsoft.Extensions.OptionsModel;
using Microsoft.Legal.MatterCenter.Models;
using Microsoft.Legal.MatterCenter.Utility;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Utilities;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;

namespace Microsoft.Legal.MatterCenter.Repository
{
    public class MatterRepository:IMatterRepository
    {
        private ISearch search;
        private ISPList spList;
        private MatterSettings matterSettings;
        private SearchSettings searchSettings;
        private CamlQueries camlQueries;
        private ListNames listNames;
        private IUsersDetails userdetails;
        private ISPOAuthorization spoAuthorization;
        private ISPPage spPage;
        private ErrorSettings errorSettings;
        /// <summary>
        /// Constructory which will inject all the related dependencies related to matter
        /// </summary>
        /// <param name="search"></param>
        public MatterRepository(ISearch search, IOptions<MatterSettings> matterSettings, 
            IOptions<SearchSettings> searchSettings, IOptions<ListNames> listNames, ISPOAuthorization spoAuthorization,
            ISPList spList, IOptions<CamlQueries> camlQueries, IUsersDetails userdetails, IOptions<ErrorSettings> errorSettings, ISPPage spPage)
        {
            this.search = search;
            this.matterSettings = matterSettings.Value;
            this.searchSettings = searchSettings.Value;
            this.listNames = listNames.Value;
            this.spList = spList;
            this.camlQueries = camlQueries.Value;
            this.userdetails = userdetails;
            this.spoAuthorization = spoAuthorization;
            this.spPage = spPage;
            this.errorSettings = errorSettings.Value;
        }

        public IList<FieldUserValue> ResolveUserNames(Client client, IList<string> userNames)
        {
            return userdetails.ResolveUserNames(client, userNames);
        }

        /// <summary>
        /// This method will try to fetch all the matters that are provisioned by the user
        /// </summary>
        /// <param name="searchRequestVM"></param>
        /// <returns></returns>
        public async Task<SearchResponseVM> GetMattersAsync(SearchRequestVM searchRequestVM)
        {
            return await Task.FromResult(search.GetMatters(searchRequestVM));
        }

        public bool AddItem(ClientContext clientContext, List list, IList<string> columns, IList<object> values)
        {
            return spList.AddItem(clientContext, list, columns, values);
        }

        /// <summary>
        /// This method will try to fetch all the matters that are provisioned by the user
        /// </summary>
        /// <param name="searchRequestVM"></param>
        /// <returns></returns>
        public async Task<PinResponseVM> GetPinnedRecordsAsync(Client client)
        {
            return await Task.FromResult(search.GetPinnedData(client, listNames.UserPinnedMatterListName,
                searchSettings.PinnedListColumnMatterDetails, false));
        }

        public IList<string> RoleCheck(string url, string listName, string columnName)
        {
            ListItemCollection collListItem = spList.GetData(matterSettings.CentralRepositoryUrl,
                listNames.DMSRoleListName,
                camlQueries.DMSRoleQuery);
            IList<string> roles = new List<string>();
            roles = collListItem.AsEnumerable().Select(roleList => Convert.ToString(roleList[matterSettings.RoleListColumnRoleName], 
                CultureInfo.InvariantCulture)).ToList();
            //if (matter.Roles.Except(roles).Count() > 0)
            //{
            //    returnValue = string.Format(CultureInfo.InvariantCulture, ConstantStrings.ServiceResponse, TextConstants.IncorrectInputUserRolesCode, TextConstants.IncorrectInputUserRolesMessage);
            //}
            return roles;
        }

        public GenericResponseVM  DeleteMatter(Client client, Matter matter)
        {
            GenericResponseVM genericResponse = null;
            using (ClientContext clientContext = spoAuthorization.GetClientContext(client.Url))
            {
                string stampResult = spList.GetPropertyValueForList(clientContext, matter.Name, matterSettings.StampedPropertySuccess);
                if (0 != string.Compare(stampResult, ServiceConstants.TRUE, StringComparison.OrdinalIgnoreCase))
                {
                    IList<string> lists = new List<string>();
                    lists.Add(matter.Name);
                    lists.Add(string.Concat(matter.Name, matterSettings.CalendarNameSuffix));
                    lists.Add(string.Concat(matter.Name, matterSettings.OneNoteLibrarySuffix));
                    lists.Add(string.Concat(matter.Name, matterSettings.TaskNameSuffix));
                    bool bListDeleted = spList.Delete(clientContext, lists);
                    if (bListDeleted)
                    {
                        //result = string.Format(CultureInfo.InvariantCulture, ConstantStrings.ServiceResponse, ServiceConstantStrings.DeleteMatterCode, TextConstants.MatterDeletedSuccessfully);
                        genericResponse = ServiceUtility.GenericResponse(matterSettings.DeleteMatterCode, matterSettings.MatterDeletedSuccessfully);
                    }
                    else
                    {
                        //result = string.Format(CultureInfo.InvariantCulture, ConstantStrings.ServiceResponse, ServiceConstantStrings.DeleteMatterCode, ServiceConstantStrings.MatterNotPresent);
                        genericResponse = ServiceUtility.GenericResponse(matterSettings.DeleteMatterCode, matterSettings.MatterNotPresent);
                    }
                    Uri clientUri = new Uri(client.Url);
                    string matterLandingPageUrl = string.Concat(clientUri.AbsolutePath, ServiceConstants.FORWARD_SLASH, 
                        matterSettings.MatterLandingPageRepositoryName.Replace(ServiceConstants.SPACE, string.Empty), 
                        ServiceConstants.FORWARD_SLASH, matter.MatterGuid, ServiceConstants.ASPX_EXTENSION);
                    spPage.Delete(clientContext, matterLandingPageUrl);
                    return ServiceUtility.GenericResponse("", "Matter updated successfully");
                }
                else
                {               
                    return ServiceUtility.GenericResponse(errorSettings.MatterLibraryExistsCode,
                         errorSettings.ErrorDuplicateMatter + ServiceConstants.MATTER + ServiceConstants.DOLLAR + 
                         MatterPrerequisiteCheck.LibraryExists);
                }
            }
        }

        /// <summary>
        /// Create a new pin for the information that has been passed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pinData"></param>
        /// <returns></returns>
        public async Task<bool> PinRecordAsync<T>(T pinRequestData)
        {
            PinRequestMatterVM pinRequestMatterVM = (PinRequestMatterVM)Convert.ChangeType(pinRequestData, typeof(PinRequestMatterVM));
            var matterData = pinRequestMatterVM.MatterData;            
            matterData.MatterName = ServiceUtility.EncodeValues(matterData.MatterName);
            matterData.MatterDescription = ServiceUtility.EncodeValues(matterData.MatterDescription);
            matterData.MatterCreatedDate = ServiceUtility.EncodeValues(matterData.MatterCreatedDate);
            matterData.MatterUrl = ServiceUtility.EncodeValues(matterData.MatterUrl);
            matterData.MatterPracticeGroup = ServiceUtility.EncodeValues(matterData.MatterPracticeGroup);
            matterData.MatterAreaOfLaw = ServiceUtility.EncodeValues(matterData.MatterAreaOfLaw);
            matterData.MatterSubAreaOfLaw = ServiceUtility.EncodeValues(matterData.MatterSubAreaOfLaw);
            matterData.MatterClientUrl = ServiceUtility.EncodeValues(matterData.MatterClientUrl);
            matterData.MatterClient = ServiceUtility.EncodeValues(matterData.MatterClient);
            matterData.MatterClientId = ServiceUtility.EncodeValues(matterData.MatterClientId);
            matterData.HideUpload = ServiceUtility.EncodeValues(matterData.HideUpload);
            matterData.MatterID = ServiceUtility.EncodeValues(matterData.MatterID);
            matterData.MatterResponsibleAttorney = ServiceUtility.EncodeValues(matterData.MatterResponsibleAttorney);
            matterData.MatterModifiedDate = ServiceUtility.EncodeValues(matterData.MatterModifiedDate);
            matterData.MatterGuid = ServiceUtility.EncodeValues(matterData.MatterGuid);
            pinRequestMatterVM.MatterData = matterData;
            return await Task.FromResult(search.PinMatter(pinRequestMatterVM));
        }

        /// <summary>
        /// Unpin the matter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pinRequestData"></param>
        /// <returns></returns>
        public async Task<bool> UnPinRecordAsync<T>(T pinRequestData)
        {
            PinRequestMatterVM pinRequestMatterVM = (PinRequestMatterVM)Convert.ChangeType(pinRequestData, typeof(PinRequestMatterVM));
            return await Task.FromResult(search.UnPinMatter(pinRequestMatterVM));
        }

        /// <summary>
        /// Get the folder hierarchy
        /// </summary>
        /// <param name="matterData"></param>
        /// <returns></returns>
        public async Task<List<FolderData>> GetFolderHierarchyAsync(MatterData matterData)
        {
            return await Task.FromResult(spList.GetFolderHierarchy(matterData));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public async Task<IList<Role>> GetRolesAsync(Client client)
        {
            IList<Role> roles = new List<Role>();
            ListItemCollection collListItem = await Task.FromResult(spList.GetData(client, listNames.DMSRoleListName, camlQueries.DMSRoleQuery));
            if (null != collListItem && 0 != collListItem.Count)
            {
                foreach (ListItem item in collListItem)
                {
                    Role tempRole = new Role();
                    tempRole.Id = Convert.ToString(item[matterSettings.ColumnNameGuid], CultureInfo.InvariantCulture);
                    tempRole.Name = Convert.ToString(item[matterSettings.RoleListColumnRoleName], CultureInfo.InvariantCulture);
                    tempRole.Mandatory = Convert.ToBoolean(item[matterSettings.RoleListColumnIsRoleMandatory], CultureInfo.InvariantCulture);
                    roles.Add(tempRole);
                }
            }
            return roles;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public async Task<IList<Role>> GetPermissionLevelsAsync(Client client)
        {
            IList<Role> roles = new List<Role>();
            List<RoleDefinition> roleDefinitions =  await Task.FromResult(search.GetWebRoleDefinitions(client));
            if (roleDefinitions.Count!=0)
            {
                foreach (RoleDefinition role in roleDefinitions)
                {
                    Role tempRole = new Role();
                    tempRole.Name = role.Name;
                    tempRole.Id = Convert.ToString(role.Id, CultureInfo.InvariantCulture);
                    roles.Add(tempRole);
                }
            }
            return roles;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public async Task<IList<Users>> GetUsersAsync(SearchRequestVM searchRequestVM)
        {
            IList<PeoplePickerUser> foundUsers = await Task.FromResult(search.SearchUsers(searchRequestVM));
            IList<Users> users = new List<Users>();
            if(foundUsers!=null && foundUsers.Count!=0)
            {
                foreach (PeoplePickerUser item in foundUsers)
                {
                    Users tempUser = new Users();
                    tempUser.Name = Convert.ToString(item.DisplayText, CultureInfo.InvariantCulture);
                    tempUser.LogOnName = Convert.ToString(item.Key, CultureInfo.InvariantCulture);
                    tempUser.Email = string.Equals(item.EntityType, ServiceConstants.PeoplePickerEntityTypeUser, StringComparison.OrdinalIgnoreCase) ? 
                        Convert.ToString(item.Description, CultureInfo.InvariantCulture) : Convert.ToString(item.EntityData.Email, CultureInfo.InvariantCulture);
                    tempUser.EntityType = Convert.ToString(item.EntityType, CultureInfo.InvariantCulture);
                    users.Add(tempUser);
                }
                return users;
            }            
            return users;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="siteCollectionUrl"></param>
        /// <returns></returns>
        public async Task<GenericResponseVM> GetConfigurationsAsync(string siteCollectionUrl)
        {
            return await Task.FromResult(search.GetConfigurations(siteCollectionUrl, listNames.MatterConfigurationsList));
        }

        public List<Tuple<int, Principal>> CheckUserSecurity(Client client, Matter matter, IList<string> userIds)
        {
            List<Tuple<int, Principal>> teamMemberPrincipalCollection = userdetails.GetUserPrincipal(client, matter, userIds);
            return teamMemberPrincipalCollection;

        }

        public PropertyValues GetStampedProperties(ClientContext clientContext, string libraryname)
        {
            return spList.GetListProperties(clientContext, libraryname);
        }

        public Users GetLoggedInUserDetails(ClientContext clientContext)
        {
            return userdetails.GetLoggedInUserDetails(clientContext);
        }

        public IEnumerable<RoleAssignment> FetchUserPermissionForLibrary(ClientContext clientContext, string libraryname)
        {
            return spList.FetchUserPermissionForLibrary(clientContext, libraryname);
        }

        public string GetMatterName(ClientContext clientContext, string matterName)
        {
            PropertyValues propertyValues = spList.GetListProperties(clientContext, matterName);
            return propertyValues.FieldValues.ContainsKey(matterSettings.StampedPropertyMatterGUID) ? 
                WebUtility.HtmlDecode(Convert.ToString(propertyValues.FieldValues[matterSettings.StampedPropertyMatterGUID], CultureInfo.InvariantCulture)) : matterName;
        }

        public int RetrieveItemId(ClientContext clientContext, string libraryName, string originalMatterName)
        {
            int listItemId = -1;
            ListItemCollection listItemCollection = spList.GetData(clientContext, libraryName);
            clientContext.Load(listItemCollection, listItemCollectionProperties => 
                                listItemCollectionProperties.Include(
                                    listItemProperties => listItemProperties.Id, 
                                    listItemProperties => listItemProperties.DisplayName));
            clientContext.ExecuteQuery();

            ListItem listItem = listItemCollection.Cast<ListItem>().FirstOrDefault(listItemProperties => listItemProperties.DisplayName.ToUpper(CultureInfo.InvariantCulture).Equals(originalMatterName.ToUpper(CultureInfo.InvariantCulture)));

            if (null != listItem)
            {
                listItemId =  listItem.Id;
            }
            return listItemId;
        }

        public List<string> MatterAssociatedLists(ClientContext clientContext, string matterName, MatterConfigurations matterConfigurations = null)
        {
            List<string> lists = new List<string>();
            lists.Add(matterName);
            lists.Add(matterName + matterSettings.OneNoteLibrarySuffix);
            if (null == matterConfigurations || matterConfigurations.IsCalendarSelected)
            {
                lists.Add(matterName + matterSettings.CalendarNameSuffix);
            }
            if (null == matterConfigurations || matterConfigurations.IsTaskSelected)
            {
                lists.Add(matterName + matterSettings.TaskNameSuffix);
            }
            List<string> listExists = spList.MatterAssociatedLists(clientContext, new ReadOnlyCollection<string>(lists));
            return listExists;
        }

        /// <summary>
        /// Remove old users and assign permissions to new users.
        /// </summary>
        /// <param name="clientContext">ClientContext object</param>
        /// <param name="requestObject">RequestObject</param>
        /// <param name="client">Client object</param>
        /// <param name="matter">Matter object</param>
        /// <param name="users">List of users to remove</param>
        /// <param name="isListItem">ListItem or list</param>
        /// <param name="list">List object</param>
        /// <param name="matterLandingPageId">List item id</param>
        /// <param name="isEditMode">Add/ Edit mode</param>
        /// <returns></returns>
        public bool UpdatePermission(ClientContext clientContext, Matter matter, List<string> users, 
            string loggedInUserTitle, bool isListItem, string listName, int matterLandingPageId, bool isEditMode)
        {
            bool result = false;
            try
            {
                if (null != clientContext && !string.IsNullOrWhiteSpace(listName))
                {
                    if (isEditMode)
                    {
                        RemoveSpecificUsers(clientContext, users, loggedInUserTitle, isListItem, listName, matterLandingPageId);
                    }
                    // Add permission
                    if (!isListItem)
                    {
                        result = spList.SetPermission(clientContext, matter.AssignUserNames, matter.Permissions, listName);
                    }
                    else
                    {
                        result = spList.SetItemPermission(clientContext, matter.AssignUserNames, matterSettings.MatterLandingPageRepositoryName, 
                            matterLandingPageId, matter.Permissions);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
            // To avoid the invalid symbol error while parsing the JSON, return the response in lower case 
            return result;
        }


        /// <summary>
        /// Removes the users' permission from list or list item.
        /// </summary>
        /// <param name="clientContext">ClientContext object</param>
        /// <param name="usersToRemove">List of users</param>
        /// <param name="isListItem">ListItem or list</param>
        /// <param name="list">List object</param>
        /// <param name="matterLandingPageId">List item id</param>
        private void RemoveSpecificUsers(ClientContext clientContext, List<string> usersToRemove, string loggedInUserTitle, 
            bool isListItem, string listName, int matterLandingPageId)
        {
            try
            {
                ListItem listItem = null;
                RoleAssignmentCollection roleCollection = null;
                Microsoft.SharePoint.Client.Web web = clientContext.Web;
                List list = web.Lists.GetByTitle(listName);
                clientContext.Load(list);
                clientContext.ExecuteQuery();
                if (0 < usersToRemove.Count)
                {
                    if (isListItem)
                    {
                        // Fetch the list item
                        if (0 <= matterLandingPageId)
                        {
                            listItem = list.GetItemById(matterLandingPageId);
                            clientContext.Load(listItem, listItemProperties => 
                            listItemProperties.RoleAssignments.Include(roleAssignmentProperties => roleAssignmentProperties.Member, 
                            roleAssignmentProperties => roleAssignmentProperties.Member.Title, 
                            roleAssignmentProperties => roleAssignmentProperties.RoleDefinitionBindings.Include(roleDef => roleDef.Name, 
                                                                                                                roleDef => roleDef.BasePermissions)));
                            clientContext.ExecuteQuery();
                            roleCollection = listItem.RoleAssignments;
                        }
                    }
                    else
                    {
                        clientContext.Load(list, listProperties => 
                        listProperties.RoleAssignments.Include(roleAssignmentProperties => roleAssignmentProperties.Member, 
                        roleAssignmentProperties => roleAssignmentProperties.Member.Title, 
                        roleAssignmentProperties => roleAssignmentProperties.RoleDefinitionBindings.Include(roleDef => roleDef.Name, 
                                                                                                            roleDef => roleDef.BasePermissions)));
                        clientContext.ExecuteQuery();
                        roleCollection = list.RoleAssignments;
                    }

                    if (null != roleCollection && 0 < roleCollection.Count && 0 < usersToRemove.Count)
                    {
                        foreach (string user in usersToRemove)
                        {
                            foreach (RoleAssignment role in roleCollection)
                            {
                                List<RoleDefinition> roleDefinationList = new List<RoleDefinition>();
                                foreach (RoleDefinition roleDef in role.RoleDefinitionBindings)
                                {
                                    // Removing permission for all the user except current user with full control
                                    // Add those users in list, then traverse the list and removing all users from that list
                                    if (role.Member.Title == user && !((role.Member.Title == loggedInUserTitle) && (roleDef.Name == 
                                        matterSettings.EditMatterAllowedPermissionLevel)))
                                    {
                                        roleDefinationList.Add(roleDef);
                                    }
                                }
                                foreach (RoleDefinition roleDef in roleDefinationList)
                                {
                                    role.RoleDefinitionBindings.Remove(roleDef);
                                }
                                role.Update();
                            }
                        }
                    }
                    clientContext.ExecuteQuery();
                }
            }
            catch (Exception)
            {
                throw;
            }

        }

        public bool UpdateMatterStampedProperties(ClientContext clientContext, MatterDetails matterDetails, Matter matter, PropertyValues matterStampedProperties, bool isEditMode)
        {
            
            try
            {
                if (null != clientContext && null != matter && null != matterDetails && (0 < matterStampedProperties.FieldValues.Count))
                {
                    Dictionary<string, string> propertyList = new Dictionary<string, string>();

                    // Get existing stamped properties
                    string stampedUsers = GetStampPropertyValue(matterStampedProperties.FieldValues, matterSettings.StampedPropertyMatterCenterUsers);
                    string stampedPermissions = GetStampPropertyValue(matterStampedProperties.FieldValues, matterSettings.StampedPropertyMatterCenterPermissions);
                    string stampedRoles = GetStampPropertyValue(matterStampedProperties.FieldValues, matterSettings.StampedPropertyMatterCenterRoles);
                    string stampedResponsibleAttorneys = GetStampPropertyValue(matterStampedProperties.FieldValues, matterSettings.StampedPropertyResponsibleAttorney);
                    string stampedTeamMembers = GetStampPropertyValue(matterStampedProperties.FieldValues, matterSettings.StampedPropertyTeamMembers);
                    string stampedBlockedUploadUsers = GetStampPropertyValue(matterStampedProperties.FieldValues, matterSettings.StampedPropertyBlockedUploadUsers);

                    string currentPermissions = string.Join(ServiceConstants.DOLLAR + ServiceConstants.PIPE + ServiceConstants.DOLLAR, matter.Permissions.Where(user => !string.IsNullOrWhiteSpace(user)));
                    string currentRoles = string.Join(ServiceConstants.DOLLAR + ServiceConstants.PIPE + ServiceConstants.DOLLAR, matter.Roles.Where(user => !string.IsNullOrWhiteSpace(user)));
                    string currentBlockedUploadUsers = string.Join(ServiceConstants.SEMICOLON, matterDetails.UploadBlockedUsers.Where(user => !string.IsNullOrWhiteSpace(user)));
                    string currentUsers = GetMatterAssignedUsers(matter);

                    string finalMatterPermissions = string.IsNullOrWhiteSpace(stampedPermissions) || isEditMode ? currentPermissions : string.Concat(stampedPermissions, ServiceConstants.DOLLAR + ServiceConstants.PIPE + ServiceConstants.DOLLAR, currentPermissions);
                    string finalMatterRoles = string.IsNullOrWhiteSpace(stampedRoles) || isEditMode ? currentRoles : string.Concat(stampedRoles, ServiceConstants.DOLLAR + ServiceConstants.PIPE + ServiceConstants.DOLLAR, currentRoles);
                    string finalResponsibleAttorneys = string.IsNullOrWhiteSpace(stampedResponsibleAttorneys) || isEditMode ? matterDetails.ResponsibleAttorney : string.Concat(stampedResponsibleAttorneys, ServiceConstants.SEMICOLON, matterDetails.ResponsibleAttorney);
                    string finalTeamMembers = string.IsNullOrWhiteSpace(stampedTeamMembers) || isEditMode ? matterDetails.TeamMembers : string.Concat(stampedTeamMembers, ServiceConstants.SEMICOLON, matterDetails.TeamMembers);
                    string finalMatterCenterUsers = string.IsNullOrWhiteSpace(stampedUsers) || isEditMode ? currentUsers : string.Concat(stampedUsers, ServiceConstants.DOLLAR + ServiceConstants.PIPE + ServiceConstants.DOLLAR, currentUsers);
                    string finalBlockedUploadUsers = string.IsNullOrWhiteSpace(stampedBlockedUploadUsers) || isEditMode ? currentBlockedUploadUsers : string.Concat(stampedBlockedUploadUsers, ServiceConstants.SEMICOLON, currentBlockedUploadUsers);

                    propertyList.Add(matterSettings.StampedPropertyResponsibleAttorney, WebUtility.HtmlEncode(finalResponsibleAttorneys));
                    propertyList.Add(matterSettings.StampedPropertyTeamMembers, WebUtility.HtmlEncode(finalTeamMembers));
                    propertyList.Add(matterSettings.StampedPropertyBlockedUploadUsers, WebUtility.HtmlEncode(finalBlockedUploadUsers));
                    propertyList.Add(matterSettings.StampedPropertyMatterCenterRoles, WebUtility.HtmlEncode(finalMatterRoles));
                    propertyList.Add(matterSettings.StampedPropertyMatterCenterPermissions, WebUtility.HtmlEncode(finalMatterPermissions));
                    propertyList.Add(matterSettings.StampedPropertyMatterCenterUsers, WebUtility.HtmlEncode(finalMatterCenterUsers));

                    spList.SetPropertBagValuesForList(clientContext, matterStampedProperties, matter.Name, propertyList);
                    return true;
                }
            }
            catch (Exception)
            {
                throw; //// This will transfer control to catch block of parent function.
            }
            return false;
        }

        public void SetPropertBagValuesForList(ClientContext clientContext, PropertyValues props, string matterName, Dictionary<string, string> propertyList)
        {
            spList.SetPropertBagValuesForList(clientContext, props, matterName, propertyList);
        }

        /// <summary>
        /// Assign or Remove Full Control base on parameter given.
        /// </summary>
        /// <param name="clientContext">Client context object</param>
        /// <param name="matter">Matter object</param>
        /// <param name="loggedInUser">Name of logged in user</param>
        /// <param name="listExists">List of existed list</param>
        /// <param name="listItemId">ID of the list</param>
        /// <param name="assignFullControl">Flag to determine Assign or Remove Permission</param>
        public void AssignRemoveFullControl(ClientContext clientContext, Matter matter, string loggedInUser, 
            int listItemId, List<string> listExists, bool assignFullControl, bool hasFullPermission)
        {
            IList<IList<string>> currentUser = new List<IList<string>>();
            IList<string> currentLoggedInUser = new List<string>() { loggedInUser };
            currentUser.Add(currentLoggedInUser);

            IList<string> permission = new List<string>() { matterSettings.EditMatterAllowedPermissionLevel };

            if (assignFullControl)
            {
                //Assign full control to Matter
                if (listExists.Contains(matter.Name))
                {
                    spList.SetPermission(clientContext, currentUser, permission, matter.Name);
                }
                //Assign full control to OneNote
                if (listExists.Contains(matter.Name + matterSettings.OneNoteLibrarySuffix))
                {
                    spList.SetPermission(clientContext, currentUser, permission, matter.Name + matterSettings.OneNoteLibrarySuffix);
                }
                // Assign full control to Task list 
                if (listExists.Contains(matter.Name + matterSettings.TaskNameSuffix))
                {
                    spList.SetPermission(clientContext, currentUser, permission, matter.Name + matterSettings.TaskNameSuffix);
                }
                //Assign full control to calendar 
                if (listExists.Contains(matter.Name + matterSettings.CalendarNameSuffix))
                {
                    spList.SetPermission(clientContext, currentUser, permission, matter.Name + matterSettings.CalendarNameSuffix);
                }
                // Assign full control to Matter Landing page
                if (0 <= listItemId)
                {
                    spList.SetItemPermission(clientContext, currentUser, matterSettings.MatterLandingPageRepositoryName, listItemId, permission);
                }
            }
            else
            {
                if (!hasFullPermission)
                {
                    //Remove full control to Matter
                    if (listExists.Contains(matter.Name))
                    {
                        RemoveFullControl(clientContext, matter.Name, loggedInUser, false, -1);
                    }
                    //Remove full control to OneNote
                    if (listExists.Contains(matter.Name + matterSettings.OneNoteLibrarySuffix))
                    {
                        RemoveFullControl(clientContext, matter.Name + matterSettings.OneNoteLibrarySuffix, loggedInUser, false, -1);
                    }
                    // Remove full control to Task list 
                    if (listExists.Contains(matter.Name + matterSettings.TaskNameSuffix))
                    {
                        RemoveFullControl(clientContext, matter.Name + matterSettings.TaskNameSuffix, loggedInUser, false, -1);
                    }
                    //Remove full control to calendar 
                    if (listExists.Contains(matter.Name + matterSettings.CalendarNameSuffix))
                    {
                        RemoveFullControl(clientContext, matter.Name + matterSettings.CalendarNameSuffix, loggedInUser, false, -1);
                    }
                    if (0 <= listItemId)
                    {
                        RemoveFullControl(clientContext, matterSettings.MatterLandingPageRepositoryName, loggedInUser, true, listItemId);
                    }
                }
            }
        }

        /// <summary>
        /// Reverts the permission of users from matter, OneNote, Calendar libraries and matter landing page
        /// </summary>
        /// <param name="requestObject">Request object</param>
        /// <param name="client">Client object</param>
        /// <param name="matter">Matter object</param>
        /// <param name="clientContext">ClientContext object</param>
        /// <param name="matterRevertListObject">MatterRevertObjectList object</param>
        /// <param name="loggedInUserTitle">Logged-in user title</param>
        /// <param name="oldUserPermissions">Old library users</param>
        /// <param name="matterLandingPageId">List item id</param>
        /// <param name="isEditMode">Add/ Edit mode</param>
        /// <returns>Status of operation</returns>
        public bool RevertMatterUpdates(Client client, Matter matter, ClientContext clientContext, 
            MatterRevertList matterRevertListObject, string loggedInUserTitle, IEnumerable<RoleAssignment> oldUserPermissions, 
            int matterLandingPageId, bool isEditMode)
        {
            bool result = false;
            try
            {
                if (null != client && null != matter && null != clientContext && null != matterRevertListObject)
                {
                    List<string> users = new List<string>();
                    users = matter.AssignUserNames.SelectMany(user => user).Distinct().ToList();

                    // Remove recently added users
                    if (null != matterRevertListObject.MatterLibrary)
                    {
                        RemoveSpecificUsers(clientContext, users, loggedInUserTitle, false, matterRevertListObject.MatterLibrary, -1);
                    }
                    if (null != matterRevertListObject.MatterCalendar)
                    {
                        RemoveSpecificUsers(clientContext, users, loggedInUserTitle, false, matterRevertListObject.MatterCalendar, -1);
                    }
                    if (null != matterRevertListObject.MatterOneNoteLibrary)
                    {
                        RemoveSpecificUsers(clientContext, users, loggedInUserTitle, false, matterRevertListObject.MatterOneNoteLibrary, -1);
                    }
                    if (null != matterRevertListObject.MatterTask)
                    {
                        RemoveSpecificUsers(clientContext, users, loggedInUserTitle, false, matterRevertListObject.MatterTask, -1);
                    }
                    if (null != matterRevertListObject.MatterSitePages)
                    {
                        RemoveSpecificUsers(clientContext, users, loggedInUserTitle, true, matterRevertListObject.MatterSitePages, matterLandingPageId);
                    }

                    if (isEditMode)
                    {
                        Matter matterRevertUserPermission = PrepareUserPermission(oldUserPermissions);
                        if (null != matterRevertListObject.MatterLibrary)
                        {
                            result = spList.SetPermission(clientContext, matterRevertUserPermission.AssignUserNames, matterRevertUserPermission.Permissions, matterRevertListObject.MatterLibrary);
                        }
                        if (null != matterRevertListObject.MatterOneNoteLibrary)
                        {
                            result = spList.SetPermission(clientContext, matterRevertUserPermission.AssignUserNames, matterRevertUserPermission.Permissions, matterRevertListObject.MatterOneNoteLibrary);
                        }
                        if (null != matterRevertListObject.MatterCalendar)
                        {
                            result = spList.SetPermission(clientContext, matterRevertUserPermission.AssignUserNames, matterRevertUserPermission.Permissions, matterRevertListObject.MatterCalendar);
                        }
                        if (null != matterRevertListObject.MatterTask)
                        {
                            result = spList.SetPermission(clientContext, matterRevertUserPermission.AssignUserNames, matterRevertUserPermission.Permissions, matterRevertListObject.MatterTask);
                        }
                        if (null != matterRevertListObject.MatterSitePages && 0 <= matterLandingPageId)
                        {
                            result = spList.SetItemPermission(clientContext, matterRevertUserPermission.AssignUserNames, matterSettings.MatterLandingPageRepositoryName, matterLandingPageId, matterRevertUserPermission.Permissions);
                        }
                    }
                }
                return result;
            }
            catch (Exception exception)
            {
                //Logger.LogError(exception, MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, ServiceConstants.LogTableName);
            }
            // To avoid the invalid symbol error while parsing the JSON, return the response in lower case
            return result;
        }


        /// <summary>
        /// Fetches the users to remove permission.
        /// </summary>
        /// <param name="userPermissions">Users having permission on library</param>
        /// <returns>Matter object containing user name and permissions</returns>
        internal Matter PrepareUserPermission(IEnumerable<RoleAssignment> userPermissions)
        {
            Matter matterUserPermission = new Matter();
            matterUserPermission.AssignUserNames = new List<IList<string>>();
            matterUserPermission.Permissions = new List<string>();

            if (null != userPermissions && 0 < userPermissions.Count())
            {
                foreach (RoleAssignment userPermission in userPermissions)
                {
                    foreach (RoleDefinition roleDefinition in userPermission.RoleDefinitionBindings)
                    {
                        matterUserPermission.AssignUserNames.Add(new List<string> { userPermission.Member.Title });
                        matterUserPermission.Permissions.Add(roleDefinition.Name);
                    }
                }
            }
            return matterUserPermission;
        }

        /// <summary>
        /// Remove Full Permission.
        /// </summary>
        /// <param name="clientContext">Client context object</param>
        /// <param name="listName">Name of the list</param>
        /// <param name="currentLoggedInUser">Name of logged in User</param>
        internal void RemoveFullControl(ClientContext clientContext, string listName, string currentLoggedInUser, bool isListItem, int matterLandingPageId)
        {
            ListItem listItem = null;
            RoleAssignmentCollection roleCollection = null;
            List list = clientContext.Web.Lists.GetByTitle(listName);
            clientContext.Load(list);
            clientContext.ExecuteQuery();
            if (isListItem)
            {
                // Fetch the list item
                if (0 <= matterLandingPageId)
                {
                    listItem = list.GetItemById(matterLandingPageId);
                    clientContext.Load(listItem, listProperties => listProperties.RoleAssignments.Include(roleAssignmentProperties => 
                        roleAssignmentProperties.Member, 
                        roleAssignmentProperties => roleAssignmentProperties.Member.Title, 
                        roleAssignmentProperties => roleAssignmentProperties.RoleDefinitionBindings.Include(roleDef => roleDef.Name, roleDef => roleDef.BasePermissions)));
                    clientContext.ExecuteQuery();
                    roleCollection = listItem.RoleAssignments;
                }
            }
            else
            {
                clientContext.Load(list, listProperties => listProperties.RoleAssignments.Include(roleAssignmentProperties => 
                    roleAssignmentProperties.Member, 
                    roleAssignmentProperties => roleAssignmentProperties.Member.Title, 
                    roleAssignmentProperties => roleAssignmentProperties.RoleDefinitionBindings.Include(roleDef => roleDef.Name, roleDef => roleDef.BasePermissions)));
                clientContext.ExecuteQuery();
                roleCollection = list.RoleAssignments;
            }


            if (null != roleCollection && 0 < roleCollection.Count)
            {
                foreach (RoleAssignment role in roleCollection)
                {
                    if (role.Member.Title == currentLoggedInUser)
                    {
                        IList<RoleDefinition> roleDefinationList = new List<RoleDefinition>();
                        foreach (RoleDefinition roleDef in role.RoleDefinitionBindings)
                        {
                            if (roleDef.Name == matterSettings.EditMatterAllowedPermissionLevel)
                            {
                                roleDefinationList.Add(roleDef);
                            }
                        }
                        foreach (RoleDefinition roleDef in roleDefinationList)
                        {
                            role.RoleDefinitionBindings.Remove(roleDef);
                        }
                    }
                    role.Update();
                }
            }
            clientContext.ExecuteQuery();

        }

        /// <summary>
        /// Checks if the property exists in property bag. Returns the value for the property from property bag.
        /// </summary>
        /// <param name="stampedPropertyValues">Dictionary object containing matter property bag key/value pairs</param>
        /// <param name="key">Key to check in dictionary</param>
        /// <returns>Property bag value for </returns>
        internal string GetStampPropertyValue(Dictionary<string, object> stampedPropertyValues, string key)
        {
            string result = string.Empty;
            if (stampedPropertyValues.ContainsKey(key))
            {
                result = WebUtility.HtmlDecode(Convert.ToString(stampedPropertyValues[key], CultureInfo.InvariantCulture));
            }

            // This is just to check for null value in key, if exists
            return (!string.IsNullOrWhiteSpace(result)) ? result : string.Empty;
        }

        /// <summary>
        /// Converts the matter users in a form that can be stamped to library.
        /// </summary>
        /// <param name="matter">Matter object</param>
        /// <returns>Users that can be stamped</returns>
        private string GetMatterAssignedUsers(Matter matter)
        {
            string currentUsers = string.Empty;
            string separator = string.Empty;
            if (null != matter && 0 < matter.AssignUserNames.Count)
            {
                foreach (IList<string> userNames in matter.AssignUserNames)
                {
                    currentUsers += separator + string.Join(ServiceConstants.SEMICOLON, userNames.Where(user => !string.IsNullOrWhiteSpace(user)));
                    separator = ServiceConstants.DOLLAR + ServiceConstants.PIPE + ServiceConstants.DOLLAR;
                }
            }
            return currentUsers;
        }
    }
}