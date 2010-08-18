﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Data.SqlClient;
using System.Configuration;
using System.Data;
using System.Data.Common;
using Essential.Data;
using System.Reflection;

namespace Essential.Diagnostics
{
    /// <summary>
    /// Trace listener that writes to a database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This listener writes to the database table created by
    /// the diagnostics_regsql tool (via the stored procedure
    /// created by the tool).
    /// </para>
    /// <para>
    /// <list type="">
    /// <listheader>Configuration options</listheader>
    /// <item>
    /// <term>initializeData</term>
    /// <value>name of the connection string of the database to write to</value>
    /// </item>
    /// <item>
    /// <term>applicationName</term>
    /// <value>Application name to use when writing to the database; set this
    /// value when the database is shared between multiple applications. The 
    /// default value is an empty string.</value>
    /// </item>
    /// <item>
    /// <term>commandText</term>
    /// <value>Command to use when calling the database. The default is
    /// the diagnostics_Trace_AddEntry stored procedure created by
    /// the diagnostics_regsql tool.</value>
    /// </item>
    /// <item>
    /// <term>maxMessageLength</term>
    /// <value>Maximum length of the message text to write to the database,
    /// where the database column has limited size. Messages (after inserting
    /// format parameters) are trimmed to this length, with the last three
    /// characters replaced by "..." if the original message was longer.</value>
    /// </item>
    /// </list>
    /// </para>
    /// </remarks>
    
    public class SqlDatabaseTraceListener : TraceListenerBase
    {
        //public const string DefaultTable = "diagnostics_Trace";
        string _connectionName;
        const string _defaultApplicationName = "";
        const string _defaultCommandText = "EXEC diagnostics_Trace_AddEntry " +
           "@ApplicationName, @Source, @Id, @EventType, @UtcDateTime, " +
           "@MachineName, @AppDomainFriendlyName, @ProcessId, @ThreadName, " +
           "@Message, @ActivityId, @RelatedActivityId, @LogicalOperationStack, " +
           "@Data;";
        const int _defaultMaxMessageLength = 1500;

        private static string[] _supportedAttributes = new string[] 
            { 
                "applicationName", "ApplicationName", "applicationname", 
                "commandText", "CommandText", "commandtext", 
                "maxMessageLength", "MaxMessageLength", "maxmessagelength", 
            };

        /// <summary>
        /// Constructor with initializeData.
        /// </summary>
        /// <param name="connectionName">name of the connection string of the database to write to</param>
        public SqlDatabaseTraceListener(string connectionName)
        {
            _connectionName = connectionName;
        }

        /// <summary>
        /// Gets or sets the name of the application used when logging to the database.
        /// </summary>
        public string ApplicationName
        {
            get
            {
                // Default format matches System.Diagnostics.TraceListener
                if (Attributes.ContainsKey("applicationname"))
                {
                    return Attributes["applicationname"];
                }
                else
                {
                    return _defaultApplicationName;
                }
            }
            set
            {
                Attributes["applicationname"] = value;
            }
        }

        /// <summary>
        /// Gets or sets the command, with parameters, sent to the database.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default command text calls the diagnostics_Trace_AddEntry stored
        /// procedure, created by the diagnostics_regsql tool.
        /// </para>
        /// <para>
        /// To bypass the stored procedure, you can directly insert by setting
        /// the command text to something like
        /// "INSERT INTO dbo.diagnostics_Trace ( ApplicationName, Source, Id, EventType, UtcDateTime, MachineName, AppDomainFriendlyName, ProcessId, ThreadName, Message, ActivityId, RelatedActivityId, LogicalOperationStack, Data ) VALUES ( @ApplicationName, @Source, @Id, @EventType, @UtcDateTime, @MachineName, @AppDomainFriendlyName, @ProcessId, @ThreadName, @Message, @ActivityId, @RelatedActivityId, @LogicalOperationStack, @Data )".
        /// </para>
        /// </remarks>
        public string CommandText
        {
            get
            {
                // Default format matches System.Diagnostics.TraceListener
                if (Attributes.ContainsKey("commandtext"))
                {
                    return Attributes["commandtext"];
                }
                else
                {
                    return _defaultCommandText;
                }
            }
            set
            {
                Attributes["commandtext"] = value;
            }
        }

        /// <summary>
        /// Gets or sets the length to trim any message text to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the maximum length of the message text to write to the database,
        /// where the database column has limited size. Messages (after inserting
        /// format parameters) are trimmed to this length, with the last three
        /// characters replaced by "..." if the original message was longer.
        /// </para>
        /// <para>
        /// A value of zero (0) can be used to remove the message limit length,
        /// for example where the column has no limit on length (e.g. a
        /// column of type ntext).
        /// </para>
        /// </remarks>
        public int MaxMessageLength
        {
            get
            {
                // Default format matches System.Diagnostics.TraceListener
                if (Attributes.ContainsKey("maxmessagelength"))
                {
                    int value;
                    if (!int.TryParse(Attributes["maxmessagelength"], out value))
                    {
                        value = _defaultMaxMessageLength;
                    }
                    return value;
                }
                else
                {
                    return _defaultMaxMessageLength;
                }
            }
            set
            {
                Attributes["maxmessagelength"] = value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Gets the name of the connection string that identifies the database to use.
        /// </summary>
        public string ConnectionName
        {
            get { return _connectionName; }
        }

        /// <summary>
        /// Allowed attributes for this trace listener.
        /// </summary>
        protected override string[] GetSupportedAttributes()
        {
            return _supportedAttributes;
        }

        /// <summary>
        /// Write trace event with data.
        /// </summary>
        protected override void WriteTrace(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message, Guid? relatedActivityId, object[] data)
        {
            string dataString = null;
            if (data != null)
            {
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    if (i != 0)
                    {
                        builder.Append(", ");
                    }
                    if (data[i] != null)
                    {
                        builder.Append(data[i]);
                    }
                }
                dataString = builder.ToString();
            }
            WriteToDatabase(eventCache, source, eventType, id, message, relatedActivityId, dataString);
        }

        private void WriteToDatabase(TraceEventCache eventCache, string source, TraceEventType eventType, int? id, string message, Guid? relatedActivityId, string dataString)
        {
            DateTime logTime;
            string logicalOperationStack = null;
            if (eventCache != null)
            {
                logTime = eventCache.DateTime.ToUniversalTime();
                if( eventCache.LogicalOperationStack != null && eventCache.LogicalOperationStack.Count > 0 )
                {
                    StringBuilder stackBuilder = new StringBuilder();
                    foreach (object o in eventCache.LogicalOperationStack)
                    {
                        if( stackBuilder.Length > 0 ) stackBuilder.Append(", ");
                        stackBuilder.Append(o);
                    }
                    logicalOperationStack = stackBuilder.ToString();
                }
            }
            else
            {
                logTime = DateTimeOffset.UtcNow.UtcDateTime;
            }

            // Truncate message
            int maxLength = MaxMessageLength;
            const string trimmedMessageIndicator = "...";
            if (message != null && message.Length > maxLength)
            {
                message = message.Substring(0, maxLength - trimmedMessageIndicator.Length) + trimmedMessageIndicator;
            }
            
            ConnectionStringSettings connectionSettings = ConfigurationManager.ConnectionStrings[ConnectionName];
            DbProviderFactory dbFactory = DbProviderFactories.GetFactory(connectionSettings.ProviderName);
            using (var connection = dbFactory.CreateConnection(connectionSettings.ConnectionString))
            {
                // TODO: Would it be more efficient to create command & params once, then set value & reuse?
                // (But would need to synchronise threading)
                using (var command = dbFactory.CreateCommand(CommandText, connection))
                {
                    command.Parameters.Add(dbFactory.CreateParameter("@ApplicationName", ApplicationName != null ? (object)ApplicationName : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@Source", source != null ? (object)source : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@Id", id ?? 0));
                    command.Parameters.Add(dbFactory.CreateParameter("@EventType", eventType.ToString()));
                    command.Parameters.Add(dbFactory.CreateParameter("@UtcDateTime", logTime));
                    command.Parameters.Add(dbFactory.CreateParameter("@MachineName", Environment.MachineName));
                    command.Parameters.Add(dbFactory.CreateParameter("@AppDomainFriendlyName", AppDomain.CurrentDomain.FriendlyName));
                    command.Parameters.Add(dbFactory.CreateParameter("@ProcessId", eventCache != null ? (object)eventCache.ProcessId : 0));
                    command.Parameters.Add(dbFactory.CreateParameter("@ThreadName", eventCache != null ? (object)eventCache.ThreadId : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@Message", message != null ? (object)message : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@ActivityId", Trace.CorrelationManager.ActivityId != Guid.Empty ? (object)Trace.CorrelationManager.ActivityId : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@RelatedActivityId", relatedActivityId.HasValue ? (object)relatedActivityId.Value : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@LogicalOperationStack", logicalOperationStack != null ? (object)logicalOperationStack : DBNull.Value));
                    command.Parameters.Add(dbFactory.CreateParameter("@Data", dataString != null ? (object)dataString : DBNull.Value));

                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
        }
    }

}