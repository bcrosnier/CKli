using CK.Core;
using CKli.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliStackList : Command
{
    public CKliStackList()
        : base( null,
                "stack list",
                "Lists all registered stacks with their name and location on the filesystem.",
                [], [],
                [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );

        var stacks = StackRepository.GetAllRegisteredStacks( monitor );

        if( stacks.Count == 0 )
        {
            context.Screen.Display( context.Screen.ScreenType.Text( "No stacks registered yet. Use 'ckli clone <stack-url>' to clone a stack." ) );
            return ValueTask.FromResult( true );
        }

        var screenType = context.Screen.ScreenType;

        // Create a table-like display
        var display = screenType.Unit.AddBelow(
            stacks.OrderBy( s => s.Path.RemoveLastPart().LastPart ) // Sort by stack name
                  .Select( s =>
                  {
                      var stackRoot = s.Path.RemoveLastPart();
                      var stackName = stackRoot.LastPart;
                      var isPublic = s.Path.LastPart == StackRepository.PublicStackName;
                      var isDuplicate = stackName.StartsWith( StackRepository.DuplicatePrefix );

                      if( isDuplicate )
                      {
                          stackName = stackName.Substring( StackRepository.DuplicatePrefix.Length );
                      }

                      var nameDisplay = screenType.Text( stackName )
                                                  .AddRight( isDuplicate ? screenType.Text( " (duplicate)", new TextStyle( ConsoleColor.Yellow, ConsoleColor.Black ) ) : screenType.Unit )
                                                  .Box( paddingRight: 1 );

                      var typeDisplay = screenType.Text( isPublic ? "Public" : "Private" )
                                                  .Box( paddingRight: 1 );

                      var pathDisplay = screenType.Text( stackRoot )
                                                  .HyperLink( new System.Uri( stackRoot ) )
                                                  .Box( paddingRight: 1 );

                      var urlDisplay = screenType.Text( s.Uri.ToString() )
                                                 .HyperLink( s.Uri )
                                                 .Box( paddingRight: 1 );

                      return nameDisplay.AddRight( typeDisplay )
                                        .AddRight( pathDisplay )
                                        .AddRight( urlDisplay );
                  } )
        ).TableLayout();

        context.Screen.Display( display );
        return ValueTask.FromResult( true );
    }
}
