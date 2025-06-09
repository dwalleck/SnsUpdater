using MediatR;
using SnsUpdater.API.Models;

namespace SnsUpdater.API.Commands
{
    public class CreatePersonCommand : IRequest<CreatePersonResult>
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
    }

    public class CreatePersonResult
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}