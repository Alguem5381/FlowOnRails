using FlowOnRails.AspNetCore.Translation;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Sample;

[ApiController]
[Route("mvc/users")]
public sealed class UsersController(UserService service) : ControllerBase
{
    [HttpGet("{id:int}")]
    public ActionResult<UserDto> Get(int id) =>
        service.Find(id).ToActionResult(user => Ok(user));
}
