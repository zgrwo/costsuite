## Summary

<!-- Brief description of what this PR does -->

## Checklist

- [ ] C# code follows project conventions (EditorConfig, .NET 8)
- [ ] All public methods have XML documentation comments
- [ ] Error handling uses structured exception handling (no bare `catch`)
- [ ] Sensitive data operations use DPAPI / AES-256 encryption
- [ ] Audit log entries added for state-changing operations
- [ ] Unit tests added/updated (`dotnet test` passes)
- [ ] BCrypt cost factor >= 12 for any new authentication paths
- [ ] Path validation applied to all file I/O operations
- [ ] [README.md](README.md) updated (if configuration or setup changes)
- [ ] [CHANGELOG.md](CHANGELOG.md) updated

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Performance improvement
- [ ] Documentation update
- [ ] Test improvement
- [ ] Refactoring (no functional change)

## Related issues

<!-- Link to issues this PR fixes: Fixes #123 -->
