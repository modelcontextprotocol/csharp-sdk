using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Extension methods for <see cref="AuthorizationConfig"/>.
/// </summary>
public static class AuthorizationConfigExtensions
{
    /// <summary>
    /// Configures the authorization config to use an HTTP listener for the OAuth authorization code flow.
    /// </summary>
    /// <param name="config">The authorization configuration to modify.</param>
    /// <param name="openBrowser">Optional function to open a browser. If not provided, a default implementation will be used.</param>
    /// <param name="hostname">The hostname to listen on. Defaults to "localhost".</param>
    /// <param name="listenPort">The port to listen on. Defaults to 8888.</param>
    /// <param name="redirectPath">The redirect path for the HTTP listener. Defaults to "/callback".</param>
    /// <returns>The modified authorization configuration for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures the authorization configuration to use an HTTP listener for the OAuth
    /// authorization code flow. When authorization is required, the listener will automatically:
    /// </para>
    /// <list type="bullet">
    ///   <item>Start an HTTP listener on the specified hostname and port</item>
    ///   <item>Open the user's browser to the authorization URL</item>
    ///   <item>Wait for the authorization code to be received via the redirect URI</item>
    ///   <item>Return the authorization code to the SDK to complete the flow</item>
    /// </list>
    /// <para>
    /// This provides a seamless authorization experience without requiring manual user intervention
    /// to copy/paste authorization codes.
    /// </para>
    /// </remarks>
    public static AuthorizationConfig UseHttpListener(
        this AuthorizationConfig config,
        Func<string, Task>? openBrowser = null,
        string hostname = "localhost",
        int listenPort = 8888,
        string redirectPath = "/callback")
    {
        // Set the redirect URI
        config.RedirectUri = new Uri($"http://{hostname}:{listenPort}{redirectPath}");
        
        // Use default browser-opening implementation if none provided
        openBrowser ??= DefaultOpenBrowser;
        
        // Configure the handler
        config.AuthorizationHandler = OAuthAuthorizationHelpers.CreateHttpListenerCallback(
            openBrowser,
            hostname,
            listenPort,
            redirectPath);
        
        return config;
    }
    
    /// <summary>
    /// Default implementation to open a URL in the default browser.
    /// </summary>
    private static Task DefaultOpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, use the built-in Process.Start for URLs
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // On Linux, use xdg-open
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On macOS, use open
                Process.Start("open", url);
            }
            else
            {
                // Fallback for other platforms
                throw new NotSupportedException("Automatic browser opening is not supported on this platform.");
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(new InvalidOperationException($"Failed to open browser: {ex.Message}", ex));
        }
    }
}