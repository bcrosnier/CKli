using CK.Core;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
using System.Text;

namespace CKli.Core;

public sealed partial class StackRepository
{
    /// <summary>
    /// The registry file name is in <see cref="Environment.SpecialFolder.LocalApplicationData"/>/CKli folder.
    /// </summary>
    public const string StackRegistryFileName = "StackRepositoryRegistry.v0.txt";

    /// <summary>
    /// Clears the <see cref="StackRegistryFileName"/>.
    /// <para>
    /// This is exposed mainly for tests.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool ClearRegistry( IActivityMonitor monitor ) => Registry.ClearRegistry( monitor );

    /// <summary>
    /// Gets all registered stacks from the <see cref="StackRegistryFileName"/>.
    /// <para>
    /// This automatically validates and cleans up stale entries (deleted directories).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>A read-only list of registered stacks with their paths and URIs.</returns>
    public static IReadOnlyList<(NormalizedPath Path, Uri Uri)> GetAllRegisteredStacks( IActivityMonitor monitor ) => Registry.GetAllStacks( monitor );

    static class Registry
    {
        static NormalizedPath _regFilePath;

        static Registry()
        {
            CKliRootEnv.CheckInitialized();
            _regFilePath = CKliRootEnv.AppLocalDataPath.AppendPart( StackRegistryFileName );
        }

        public static IReadOnlyList<NormalizedPath> CheckExistingStack( IActivityMonitor monitor, Uri stackUri )
        {
            FindOrUpdate( monitor, default, stackUri, out var found );
            return found!;
        }

        public static void RegisterNewStack( IActivityMonitor monitor, NormalizedPath path, Uri stackUri )
        {
            FindOrUpdate( monitor, path, stackUri, out var _ );
        }

        public static bool ClearRegistry( IActivityMonitor monitor )
        {
            using Mutex mutex = CKliRootEnv.AcquireAppMutex( monitor );
            return FileHelper.DeleteFile( monitor, _regFilePath );
        }

        public static IReadOnlyList<(NormalizedPath Path, Uri Uri)> GetAllStacks( IActivityMonitor monitor )
        {
            using Mutex mutex = CKliRootEnv.AcquireAppMutex( monitor );
            var result = new List<(NormalizedPath Path, Uri Uri)>();

            if( !File.Exists( _regFilePath ) )
            {
                return result;
            }

            var map = new Dictionary<NormalizedPath, Uri>();
            bool mustSave = false;

            foreach( var line in File.ReadLines( _regFilePath ) )
            {
                try
                {
                    var s = ReadOneLine( monitor, line );
                    if( !Directory.Exists( s.Path ) )
                    {
                        monitor.Info( $"Stack at '{s.Path}' ({s.Uri}) has been deleted." );
                        mustSave = true;
                    }
                    else
                    {
                        if( !map.TryAdd( s.Path, s.Uri ) )
                        {
                            monitor.Warn( $"Duplicate path '{s.Path}' found. It will be deleted." );
                            mustSave = true;
                        }
                    }
                }
                catch( Exception ex )
                {
                    monitor.Warn( $"While reading line '{line}' from '{_regFilePath}'. This faulty line will be deleted.", ex );
                    mustSave = true;
                }
            }

            if( mustSave )
            {
                monitor.Trace( $"Updating file '{_regFilePath}' with {map.Count} stacks after cleanup." );
                SaveRegistry( monitor, map );
            }

            foreach( var (path, uri) in map )
            {
                result.Add( (path, uri) );
            }

            return result;
        }

        static void SaveRegistry( IActivityMonitor monitor, Dictionary<NormalizedPath, Uri> map )
        {
            var b = new StringBuilder();
            foreach( var (path, uri) in map )
            {
                b.Append( path ).Append( '*' ).Append( uri ).AppendLine();
            }
            File.WriteAllText( _regFilePath, b.ToString() );
        }

        static (NormalizedPath Path, Uri Uri) ReadOneLine( IActivityMonitor monitor, string line )
        {
            var s = line.Split( '*', StringSplitOptions.TrimEntries );
            var gitPath = new NormalizedPath( s[0] );
            if( gitPath.Parts.Count <= 3 ) Throw.InvalidDataException( $"Too short path: '{gitPath}'." );
            if( gitPath.LastPart != PublicStackName && gitPath.LastPart != PrivateStackName )
            {
                Throw.InvalidDataException( $"Invalid path: '{gitPath}'. Must end with '{PublicStackName}' or '{PrivateStackName}'." );
            }
            var url = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( new Uri( s[1], UriKind.Absolute ) );
            return (gitPath, url);
        }

        static void FindOrUpdate( IActivityMonitor monitor, NormalizedPath newPath, Uri findOrUpdateStackUri, out List<NormalizedPath>? foundPath )
        {
            foundPath = newPath.IsEmptyPath
                            ? new List<NormalizedPath>()
                            : null;
            using Mutex mutex = CKliRootEnv.AcquireAppMutex( monitor );
            var map = new Dictionary<NormalizedPath, Uri>();

            bool mustSave = !File.Exists( _regFilePath );

            if( !mustSave )
            {
                foreach( var line in File.ReadLines( _regFilePath ) )
                {
                    try
                    {
                        var s = ReadOneLine( monitor, line );
                        if( !Directory.Exists( s.Path ) )
                        {
                            monitor.Info( $"Stack at '{s.Path}' ({s.Uri}) has been deleted." );
                            mustSave = true;
                        }
                        else
                        {
                            if( !map.TryAdd( s.Path, s.Uri ) )
                            {
                                monitor.Warn( $"Duplicate path '{s.Path}' found. It will be deleted." );
                                mustSave = true;
                            }
                            else if( foundPath != null && findOrUpdateStackUri == s.Uri )
                            {
                                foundPath.Add( s.Path );
                            }
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Warn( $"While reading line '{line}' from '{_regFilePath}'. This faulty line will be deleted.", ex );
                        mustSave = true;
                    }
                }
            }

            if( foundPath == null )
            {
                map[newPath] = findOrUpdateStackUri;
                mustSave = true;
            }
            if( mustSave )
            {
                monitor.Trace( $"Updating file '{_regFilePath}' with {map.Count} stacks." );
                SaveRegistry( monitor, map );
            }
        }
    }


}
