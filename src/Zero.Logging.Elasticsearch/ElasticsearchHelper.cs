using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Elasticsearch.Net;
using Zero.Logging.Batching;

namespace Zero.Logging.Elasticsearch
{
    internal class ElasticsearchHelper
    {
        private readonly ElasticLowLevelClient _client;

        private readonly Func<LogMessage, string> _indexDecider;
        private readonly bool _registerTemplateOnStartup;
        private readonly string _templateName;
        private readonly string _templateMatchString;

        private static readonly Regex _indexFormatRegex = new Regex(@"^(.*)(?:\{0\:.+\})(.*)$");

        public static ElasticsearchHelper Create(EsLoggerOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            return new ElasticsearchHelper(options);
        }

        private ElasticsearchHelper(EsLoggerOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ElasticsearchUrl)) throw new ArgumentException("options.ElasticsearchUrl");
            if (string.IsNullOrWhiteSpace(options.IndexFormat)) throw new ArgumentException("options.IndexFormat");
            if (string.IsNullOrWhiteSpace(options.TypeName)) throw new ArgumentException("options.TypeName");
            if (string.IsNullOrWhiteSpace(options.TemplateName)) throw new ArgumentException("options.TemplateName");

            _templateName = options.TemplateName;
            _templateMatchString = _indexFormatRegex.Replace(options.IndexFormat, @"$1*$2");
            _indexDecider = options.IndexDecider ?? (logMsg => string.Format(options.IndexFormat, logMsg.Timestamp));

            Options = options;

            IConnectionPool pool;
            if (options.ElasticsearchUrl.Contains(";"))
            {
                pool = new StaticConnectionPool(options.ElasticsearchUrl.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(_ => new Uri(_)));
            }
            else
            {
                pool = new SingleNodeConnectionPool(new Uri(options.ElasticsearchUrl));
            }

            var configuration = new ConnectionConfiguration(pool, options.Connection, options.Serializer).RequestTimeout(options.ConnectionTimeout);

            if (!string.IsNullOrEmpty(options.UserName) && !string.IsNullOrEmpty(options.Password))
            {
                configuration.BasicAuthentication(options.UserName, options.Password);
            }

            if (options.ModifyConnectionSettings != null) configuration = options.ModifyConnectionSettings(configuration);

            configuration.ThrowExceptions();

            _client = new ElasticLowLevelClient(configuration);

            _registerTemplateOnStartup = options.AutoRegisterTemplate;
            TemplateRegistrationSuccess = !_registerTemplateOnStartup;
        }

        public EsLoggerOptions Options { get; }

        public IElasticLowLevelClient Client => _client;

        public bool TemplateRegistrationSuccess { get; private set; }


        public string Serialize(object o)
        {
            return _client.Serializer.SerializeToString(o, SerializationFormatting.None);
        }

        public string GetIndexForEvent(LogMessage e, DateTimeOffset offset)
        {
            if (!TemplateRegistrationSuccess && Options.RegisterTemplateFailure == RegisterTemplateRecovery.IndexToDeadletterIndex)
            {
                return string.Format(Options.DeadLetterIndexName, offset);
            }
            return _indexDecider(e);
        }
  
     
 
    }
}
