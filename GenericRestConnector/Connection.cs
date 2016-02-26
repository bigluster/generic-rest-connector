﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QlikView.Qvx.QvxLibrary;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Dynamic;
using System.Net;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Runtime.Caching;

namespace GenericRestConnector
{
    public class Connection : QvxConnection
    {
        RESTHelper helper;
        Int64 recordsLoaded;
        String liveTable;
        public String session;
        private static MemoryCache grcCache = MemoryCache.Default;

        public override void Init()
        {
            //Debugger.Launch();     
            if (helper == null && MParameters != null)
            {
                Debugger.Launch();               
                helper = new RESTHelper(MParameters);
            }
        }

        private IEnumerable<QvxDataRow> GetData()
        {
            //Debugger.Launch();
            dynamic data;
            recordsLoaded = 0;

            //Get a reference to the QvxTable from MTables
            QvxTable qTable = FindTable(liveTable, MTables);
            helper.Prep();

            while (helper.IsMore)
            {
                data = helper.GetJSON();

                if (!String.IsNullOrEmpty(helper.DataElement))
                {
                    data = data[helper.DataElement];
                }
                helper.pageInfo.CurrentPageSize = data.Count;
                helper.pageInfo.CurrentPage++;
                foreach (dynamic row in data)
                {
                    if (recordsLoaded < helper.pageInfo.LoadLimit)
                    {
                        yield return InsertRow(row, qTable);
                    }
                    else
                    {
                        helper.IsMore = false;
                        break;
                    }
                }
                helper.pageInfo.CurrentRecord = recordsLoaded;
                helper.Page();
            }
        }

        private QvxDataRow InsertRow(dynamic sourceRow, QvxTable tableDef)
        {
            QvxDataRow destRow = new QvxDataRow();
            foreach (QvxField fieldDef in tableDef.Fields)
            {
                dynamic originalDef = helper.ActiveFields[fieldDef.FieldName];
                dynamic sourceField = GetSourceValue(sourceRow, originalDef.path.ToString(), originalDef.type.ToString());
                if (sourceField != null)
                {
                    destRow[fieldDef] = sourceField.ToString();
                }
            }
            recordsLoaded++;
            return destRow;
        }

        private dynamic GetSourceValue(dynamic row, String path, String type)
        {
            dynamic result = row;
            if (path.IndexOf(".") == -1)
            {
                return result[path];
            }
            else
            {

                string[] Children = path.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);
                foreach (String s in Children)
                {
                    if (result[s] != null)
                    {
                        result = result[s];
                    }
                    else
                    {
                        return null;
                    }

                }
                return convertToType(result, type);
            }
        }

        private dynamic convertToType(dynamic value, String type)
        {
            switch (type)
            {
                case "String":
                    return value.ToString();
                case "Boolean":
                    return Boolean.Parse(value.ToString());
                case "Integer":
                    return Int32.Parse(value.ToString());
                default:
                    return value.ToString();
            }
        }

        public override QvxDataTable ExtractQuery(string query, List<QvxTable> qvxTables)
        {
            QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Notice, "Extracting Query");
            //Debugger.Launch();
            //NOTE: Where clause not yet supported
            String fields = "";
            query = query.Replace("\r\n", " ");
            try
            {
                Match match;
                if (query.ToUpper().Contains(" WHERE "))
                {
                    match = Regex.Match(query, @"SELECT\s+(?<fields>.+)\sFROM\s+(?<table>.+)\sWHERE\s+(?<where>.+)", RegexOptions.IgnoreCase);
                }
                else
                {
                    match = Regex.Match(query, @"SELECT\s+(?<fields>.+)\sFROM\s+(?<table>\w+)", RegexOptions.IgnoreCase);
                }
                if (!match.Success)
                {
                    QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Error, string.Format("ExtractQueryAndTransmitTableHeader() - QvxPleaseSendReplyException({0}, \"Invalid query: {1}\")", QvxResult.QVX_SYNTAX_ERROR, query));
                }
                //Establish Table Name
                fields = match.Groups["fields"].Value;
                liveTable = match.Groups["table"].Value;
            }
            catch (Exception ex)
            {
                QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Error, ex.Message);
            }
            if (!String.IsNullOrEmpty(liveTable) && helper!=null)
            {
                helper.SetActiveTable(liveTable);
                //Create QvxTable based on fields in Select statement
                MTables = new List<QvxTable>();
                QvxTable qT = new QvxTable { TableName = liveTable, Fields = helper.createFieldList(liveTable, fields), GetRows = GetData };
                MTables.Add(qT);
                return new QvxDataTable(qT);
            }
            return null;

            //return table
        }
    }
}