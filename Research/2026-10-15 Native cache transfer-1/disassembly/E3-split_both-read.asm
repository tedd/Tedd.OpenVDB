?read@@YA_KAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@H@Z PROC ; read, COMDAT

; 67   : {

$LN89:
	push	rbx
	push	rbp
	push	rsi
	push	rdi
	push	r14
	push	r15
	sub	rsp, 40					; 00000028H
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	r9, QWORD PTR [rdx+8]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 67   : {

	mov	r14, rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sub	r9, QWORD PTR [rdx]
	mov	rax, 3074457345618258603		; 2aaaaaaaaaaaaaabH
	imul	r9
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 68   :     uint64_t sum = 0;

	xor	esi, esi
	xor	edi, edi
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sar	rdx, 1
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 67   : {

	mov	ebp, r8d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	r15, rdx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 67   : {

	mov	rbx, rcx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	shr	r15, 63					; 0000003fH
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 69   :     const size_t mask = coords.size() - 1;

	dec	r15
	add	r15, rdx

; 70   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	test	r8d, r8d
	jle	$LN81@read
	npad	9
$LL4@read:
	mov	eax, edi
	and	rax, r15
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
	mov	rax, QWORD PTR [r14]
	lea	rdx, QWORD PTR [rax+rcx*4]
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 134  :     Int32 operator[](size_t i) const { OPENVDB_ASSERT(i < 3); return mVec[i]; }

	mov	eax, DWORD PTR [rax+rcx*4]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 1028 :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	ecx, eax
	and	ecx, -8
	cmp	ecx, DWORD PTR [rbx+536]
	jne	SHORT $LN13@read
	mov	r8d, DWORD PTR [rdx+4]
	mov	ecx, r8d
	and	ecx, -8
	cmp	ecx, DWORD PTR [rbx+540]
	jne	SHORT $LN13@read
	mov	r9d, DWORD PTR [rdx+8]
	mov	ecx, r9d
	and	ecx, -8
	cmp	ecx, DWORD PTR [rbx+544]
	jne	SHORT $LN13@read
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	eax, 7
	and	r8d, 7
	shl	eax, 3
	and	r9d, 7
	or	eax, r8d
	shl	eax, 3
	or	eax, r9d
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 460  :                 return LeafCacheT::buffer()[LeafNodeT::coordToOffset(xyz)];

	mov	ecx, eax
	mov	rax, QWORD PTR [rbx+16]
	lea	rax, QWORD PTR [rax+rcx*2]
	jmp	SHORT $LN12@read
$LN13@read:

; 461  :             return this->getValueSecondary(xyz);

	mov	rcx, rbx
	call	?getValueSecondary@?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@QEBAAEBGAEBVCoord@math@34@@Z ; openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> >::getValueSecondary
$LN12@read:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 70   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	movzx	ecx, WORD PTR [rax]
	inc	edi
	add	rsi, rcx
	cmp	edi, ebp
	jl	SHORT $LL4@read
$LN81@read:

; 71   :     return sum;
; 72   : }

	mov	rax, rsi
	add	rsp, 40					; 00000028H
	pop	r15
	pop	r14
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
?read@@YA_KAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@H@Z ENDP ; read
