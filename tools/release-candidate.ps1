function Get-CandidateFramework([string]$Path) {
    # Read PE metadata only: never execute a candidate or resolve its vendor dependencies.
    $stream = [IO.File]::OpenRead($Path)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $reader.CustomAttributes) {
            $attribute = $reader.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind.ToString() -ne 'MemberReference') { continue }
            $member = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind.ToString() -ne 'TypeReference') { continue }
            $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($reader.GetString($type.Name) -ne 'TargetFrameworkAttribute' -or
                $reader.GetString($type.Namespace) -ne 'System.Runtime.Versioning') { continue }
            $value = $reader.GetBlobReader($attribute.Value)
            if ($value.ReadUInt16() -ne 1) { throw 'Invalid target framework attribute.' }
            return $value.ReadSerializedString()
        }
        throw 'Candidate has no target framework attribute.'
    }
    finally { $pe.Dispose(); $stream.Dispose() }
}

function Assert-CandidateFramework([string]$Path) {
    if ((Get-CandidateFramework $Path) -ne '.NETCoreApp,Version=v10.0') {
        throw 'Rhino 9 candidate must target .NET 10; do not package RC1 or a Rhino 8 binary.'
    }
}
