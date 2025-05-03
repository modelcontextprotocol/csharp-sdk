namespace ModelContextProtocol.Auth;


/// <param name="CodeVerifier"> The code verifier used to generate the code challenge. </param>
/// <param name="CodeChallenge"> The code challenge sent to the authorization server. </param>
public record PkceValues(string CodeVerifier, string CodeChallenge);
