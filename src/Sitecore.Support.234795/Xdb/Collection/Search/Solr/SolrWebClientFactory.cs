using Microsoft.Extensions.Configuration;
using Sitecore.Xdb.Collection.Search.Solr;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;

namespace Sitecore.Support.Xdb.Collection.Search.Solr
{
  public class SolrWebClientFactory : IWebClientFactory
  {
    private static readonly Dictionary<WebClientProperties, HttpClient> RegisteredClients = new Dictionary<WebClientProperties, HttpClient>();
    private static readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

    private static readonly BindingFlags _bFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly MethodInfo CertificateValidator_Initialize = 
      Type.GetType("Sitecore.Xdb.Collection.Search.Solr.CertificateValidator").GetMethod("Initialize", _bFlags);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "We do not dispose the clients during the application lifecycle")]
    private static HttpClient EnsureHttpClient(WebClientProperties webClientProperties)
    {
      HttpClient result;
      try
      {
        Lock.EnterReadLock();

        if (RegisteredClients.TryGetValue(webClientProperties, out result))
        {
          return result;
        }
      }
      finally
      {
        Lock.ExitReadLock();
      }

      try
      {
        Lock.EnterUpgradeableReadLock();

        if (RegisteredClients.TryGetValue(webClientProperties, out result))
        {
          return result;
        }

        Lock.EnterWriteLock();

        result = CreateHttpClient(webClientProperties);
        RegisteredClients.Add(webClientProperties, result);
      }
      finally
      {
        if (Lock.IsWriteLockHeld)
        {
          Lock.ExitWriteLock();
        }

        if (Lock.IsUpgradeableReadLockHeld)
        {
          Lock.ExitUpgradeableReadLock();
        }
      }

      return result;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "We do not dispose the clients during the application lifecycle")]
    private static HttpClient CreateHttpClient(WebClientProperties webClientProperties)
    {
      HttpClient client;
      if (webClientProperties.Credentials != null)
      {
        var handler = new HttpClientHandler
        {
          Credentials = webClientProperties.Credentials
        };

        client = new HttpClient(handler);
      }
      else
      {
        client = new HttpClient();
      }

      client.DefaultRequestHeaders.AcceptCharset.Add(new StringWithQualityHeaderValue(webClientProperties.Encoding.WebName));

      return client;
    }

    public SolrWebClientFactory()
    {
    }

    public SolrWebClientFactory(IEnumerable<string> allowedThumbprints)
    {
      CertificateValidator_Initialize.Invoke(null, new object[] { allowedThumbprints });
    }

    public SolrWebClientFactory(IConfiguration options) : this(GetAllowedThumbprints(options))
    {
    }

    public virtual HttpClient GetHttpClient(WebClientProperties webClientProperties)
    {
      return EnsureHttpClient(webClientProperties);
    }

    private static IEnumerable<string> GetAllowedThumbprints(IConfiguration options)
    {
      const string thumbprintsCyrillicSectionName = "AсceptCertificates";
      const string thumbprintsLatinSectionName = "AcceptCertificates";
      return GetThumbprints(thumbprintsCyrillicSectionName).Concat(GetThumbprints(thumbprintsLatinSectionName));

      IEnumerable<string> GetThumbprints(string sectionName)
      {
        IConfigurationSection thumbssection = options.GetSection(sectionName);
        foreach (IConfigurationSection optionChild in thumbssection?.GetChildren() ?? Enumerable.Empty<IConfigurationSection>())
        {
          yield return optionChild.Value;
        }
      }
    }
  }
}