using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SnsUpdater.API.Events;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API.Commands
{
    public class CreatePersonCommandHandler : IRequestHandler<CreatePersonCommand, CreatePersonResult>
    {
        private readonly IMediator _mediator;

        public CreatePersonCommandHandler(IMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        }

        public async Task<CreatePersonResult> Handle(CreatePersonCommand request, CancellationToken cancellationToken)
        {
            using var activity = TelemetryConfiguration.ApiActivitySource.StartActivity("CreatePerson");
            activity?.SetTag("person.firstName", request.FirstName);
            activity?.SetTag("person.lastName", request.LastName);

            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.FirstName) || 
                    string.IsNullOrWhiteSpace(request.LastName))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
                    return new CreatePersonResult
                    {
                        Success = false,
                        Message = "First name and last name are required."
                    };
                }

                // TODO: Save person to database (placeholder for now)
                // For now, we'll simulate a successful save with a generated ID
                var personId = new Random().Next(1000, 9999);
                activity?.SetTag("person.id", personId);

                // Create the result
                var result = new CreatePersonResult
                {
                    Id = personId,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PhoneNumber = request.PhoneNumber,
                    Success = true,
                    Message = "Person created successfully."
                };

                // Publish domain event
                var personCreatedEvent = new PersonCreatedEvent
                {
                    PersonId = personId,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PhoneNumber = request.PhoneNumber,
                    CreatedAt = DateTime.UtcNow
                };

                using (var publishActivity = TelemetryConfiguration.ApiActivitySource.StartActivity("PublishPersonCreatedEvent"))
                {
                    publishActivity?.SetTag("event.type", "PersonCreated");
                    publishActivity?.SetTag("event.personId", personId);
                    await _mediator.Publish(personCreatedEvent, cancellationToken);
                }

                // Record metric
                TelemetryConfiguration.PersonsCreated.Add(1, 
                    new System.Collections.Generic.KeyValuePair<string, object>("status", "success"));

                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.RecordException(ex);
                
                TelemetryConfiguration.PersonsCreated.Add(1, 
                    new System.Collections.Generic.KeyValuePair<string, object>("status", "error"));

                return new CreatePersonResult
                {
                    Success = false,
                    Message = $"An error occurred while creating the person: {ex.Message}"
                };
            }
        }
    }
}