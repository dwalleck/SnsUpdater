using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using MediatR;
using SnsUpdater.API.Commands;
using SnsUpdater.API.Models;

namespace SnsUpdater.API.Controllers
{
    public class PeopleController : ApiController
    {
        private readonly IMediator _mediator;

        public PeopleController(IMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        }

        // GET: api/People
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/People/5
        public string Get(int id)
        {
            return "value";
        }

        // POST: api/People
        [HttpPost]
        public async Task<IHttpActionResult> Post([FromBody]Person person)
        {
            try
            {
                if (person == null)
                {
                    return BadRequest("Person data is required.");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var command = new CreatePersonCommand
                {
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    PhoneNumber = person.PhoneNumber
                };

                var result = await _mediator.Send(command);

                if (result.Success)
                {
                    return Created($"api/people/{result.Id}", result);
                }
                else
                {
                    return BadRequest(result.Message);
                }
            }
            catch (Exception ex)
            {
                // In production, use proper logging framework
                System.Diagnostics.Trace.TraceError($"Error creating person: {ex}");
                return InternalServerError(new HttpResponseException(
                    Request.CreateErrorResponse(HttpStatusCode.InternalServerError, 
                    "An error occurred while processing your request.")));
            }
        }

        // PUT: api/People/5
        public void Put(int id, [FromBody]string value)
        {
        }

        // DELETE: api/People/5
        public void Delete(int id)
        {
        }
    }
}
