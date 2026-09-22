using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace Webview2Viewer.Web
{
  // The preview page talks to the plugin over in-process "http://*.example" hosts.
  // Reads of the document and of files next to it are harmless, but writes (PUT the
  // edited document back into the editor, POST an image to disk, POST editor events)
  // must only be accepted from our own loader page and never from content that a
  // previewed document managed to smuggle into the WebView. The loader page receives
  // a random token in its options and echoes it in this header.
  internal static class WebSession
  {
    public const string TokenHeader = "X-AnotherMarkdown-Token";

    public static string NewToken()
    {
      var bytes = new byte[32];
      using (var rng = RandomNumberGenerator.Create()) {
        rng.GetBytes(bytes);
      }
      return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    public static bool IsAuthorized(CoreWebView2WebResourceRequest request, string expectedToken)
    {
      if (string.IsNullOrEmpty(expectedToken)) {
        return false;
      }
      var header = request.Headers
        .FirstOrDefault(h => h.Key.Equals(TokenHeader, StringComparison.OrdinalIgnoreCase));
      if (header.Value == null) {
        return false;
      }
      return FixedTimeEquals(header.Value, expectedToken);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
      if (a.Length != b.Length) {
        return false;
      }
      var diff = 0;
      for (var i = 0; i < a.Length; i++) {
        diff |= a[i] ^ b[i];
      }
      return diff == 0;
    }
  }
}
