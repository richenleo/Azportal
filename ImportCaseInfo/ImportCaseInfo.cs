﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;

using System.Net;
using System.IO;
using Newtonsoft.Json.Linq;

using System.Data.SqlClient;

namespace ImportCaseInfo
{
    class ImportCaseInfo : SPJobDefinition
    {
        public ImportCaseInfo() : base() { }
        public ImportCaseInfo(string jobName, SPService service)
            : base(jobName, service, null, SPJobLockType.None)
        {
            this.Title = "Import Case Info In Azure List";
        }
        public ImportCaseInfo(string jobName, SPWebApplication webapp)
            : base(jobName, webapp, null, SPJobLockType.ContentDatabase)
        {
            this.Title = "Import Case Info In Azure List";
        }
        public override void Execute(Guid targetInstanceId)
        {
            ImportInfo();
        }

        public void ImportInfo()
        {
            string siteUrl = "http://azportal";
            using (SPSite site = new SPSite(siteUrl))
            {
                using (SPWeb web = site.OpenWeb())
                {
                    SPList list = web.GetList("http://azportal/Lists/Azure Outage List");

                    // Get the item collections which the case details haven't been updated
                    SPQuery query = new SPQuery();
                    query.Query = "<Where><Neq><FieldRef Name=\"Active\" /><Value Type=\"Text\">" + "0" + "</Value></Neq></Where>";// active!=0
                    SPListItemCollection items = list.GetItems(query);

                    // Get info and fill in all items in the item collection
                    foreach (SPListItem item in items)
                    {
                        if ((string)item["Case ID"] == null)
                            continue;

                        string SRNumber = (item["Case ID"]).ToString().Trim();
                        string MSSolveBaseURL = @"https://mssolveweb.partners.extranet.microsoft.com/MSSolveWeb/Home";

                        try
                        {
                            // call the MSSolve Web Service to get the response.
                            WebRequest webRequest = WebRequest.Create(MSSolveBaseURL + "/GetSR/" + SRNumber + "/0");
                            webRequest.Timeout = 180000;
                            ((HttpWebRequest)webRequest).UserAgent = @"Mozilla/5.0 (Windows NT 6.3; WOW64; Trident/7.0; rv:11.0) like Gecko";
                            webRequest.UseDefaultCredentials = false;
                            webRequest.Credentials = CredentialCache.DefaultCredentials; // Use t-zefu credential
                            string text = string.Empty;
                            WebResponse response = webRequest.GetResponse();
                            Stream responseStream = response.GetResponseStream();
                            StreamReader streamReader = new StreamReader(responseStream);
                            text = streamReader.ReadToEnd();
                            response.Dispose();
                            responseStream.Close();
                            responseStream.Dispose();

                            // Use Newtonsoft to get the target data from json
                            JObject obj = JObject.Parse(text);
                            JObject ServiceRequest = (JObject)obj["Data"]["ServiceRequestResponseData"]["ServiceRequest"];
                            JObject EmailContact = (JObject)obj["Data"]["ServiceRequestResponseData"]["Contacts"];
                            IList<JToken> emailList = EmailContact["SRContacts"].Children().ToList();

                            // Fill out the list item with the data
                            item["Case Status"] = (string)ServiceRequest["StatusName"];
                            item["Customer Company Name"] = (string)ServiceRequest["AccountIdName"];
                            item["Customer Name"] = (string)ServiceRequest["CurrentAuthorizedContactIdName"];
                            item["Customer Contact Email"] = (string)emailList[0]["PrimaryEmail"];
                            item["Premier/BC"] = (string)ServiceRequest["ServiceLevelName"];
                            item["Owner Name"] = (string)ServiceRequest["OwnerUserName"];
                            item["TAM Name"] = (string)ServiceRequest["PrimaryAccountManagerIdName"];
                            item["Calling Country"] = (string)ServiceRequest["CallingCountryCode"];
                            item["Contract Country"] = (string)ServiceRequest["ContractCountryIdName"];

                            // Get engineer working group
                            string email = (string)ServiceRequest["OwnerUserInternalEmail"];
                            if (String.IsNullOrEmpty(email))
                            {
                                item["Engineer Working Group"] = "n/a";
                                item["Owner Name"] = "Not Assigned";
                            }
                            else
                            {
                                char delimiterChars = '@';
                                string[] words = email.Split(delimiterChars);
                                string alias = words[0];
                                string queryStr = "select distinct EmployeeEmail, workgroup from vwDimEmployee where iscurrent = 'yes' and EmployeeEmail = '" + alias + "'";
                                using (SqlConnection con = new SqlConnection(@"Server=detego-ctssql;Database=ssCTSDataMart;Integrated Security=True;"))
                                {
                                    using (SqlCommand cmd = new SqlCommand(queryStr, con))
                                    {
                                        cmd.CommandTimeout = 2000;
                                        con.Open();
                                        using (SqlDataReader reader = cmd.ExecuteReader())
                                        {
                                            while (reader.Read())
                                            {
                                                string fields = reader["workgroup"].ToString();
                                                item["Engineer Working Group"] = fields;
                                            }
                                        }
                                    }
                                }
                            }

                            item["Active"] = "0";

                            if (item != null)
                            {
                                item.Update();
                            }
                        }
                        catch
                        {
                            //errorMessage is not used, just in case of invalid case ID
                            string errorMessage = "case Id do not exist! or something else led to failure of getting other info";
                        }
                    }
                }
            }
        }
    }
}