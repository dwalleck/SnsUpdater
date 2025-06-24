using System;
using MediatR;
using Unity;
using SnsUpdater.API;
using SnsUpdater.API.Commands;
using System.Threading.Tasks;

namespace SnsUpdater
{
    class TestMediatRConfig
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Testing MediatR 12.x configuration with Unity...");
            
            try
            {
                // Initialize Unity configuration
                UnityConfig.RegisterComponents();
                var container = UnityConfig.Container;
                
                // Test resolving IMediator
                var mediator = container.Resolve<IMediator>();
                Console.WriteLine($"✓ Successfully resolved IMediator: {mediator.GetType().FullName}");
                
                // Test resolving IServiceProvider
                var serviceProvider = container.Resolve<IServiceProvider>();
                Console.WriteLine($"✓ Successfully resolved IServiceProvider: {serviceProvider.GetType().FullName}");
                
                // Test creating and sending a command
                var command = new CreatePersonCommand
                {
                    FirstName = "Test",
                    LastName = "User",
                    PhoneNumber = "555-1234"
                };
                
                Console.WriteLine("\nSending CreatePersonCommand...");
                var result = await mediator.Send(command);
                
                Console.WriteLine($"✓ Command handled successfully!");
                Console.WriteLine($"  - Success: {result.Success}");
                Console.WriteLine($"  - Message: {result.Message}");
                Console.WriteLine($"  - Person ID: {result.Id}");
                
                Console.WriteLine("\nMediatR 12.x is properly configured with Unity!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Error: {ex.GetType().Name}");
                Console.WriteLine($"  Message: {ex.Message}");
                Console.WriteLine($"  Stack: {ex.StackTrace}");
            }
        }
    }
}