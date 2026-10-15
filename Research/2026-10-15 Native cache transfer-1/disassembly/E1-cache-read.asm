?read@@YA_KAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@H@Z PROC ; read, COMDAT

; 66   : {

$LN146:
	mov	r11, rsp
	mov	QWORD PTR [r11+8], rcx
	push	rbp
	push	r12
	push	r13
	push	r15
	sub	rsp, 88					; 00000058H
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	r9, QWORD PTR [rdx+8]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 66   : {

	mov	r13, rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sub	r9, QWORD PTR [rdx]
	mov	rax, 3074457345618258603		; 2aaaaaaaaaaaaaabH
	imul	r9
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 67   :     uint64_t sum = 0;

	xor	r15d, r15d
	xor	ebp, ebp
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sar	rdx, 1
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 66   : {

	mov	r12d, r8d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	rax, rdx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 68   :     const size_t mask = coords.size() - 1;

	dec	rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	shr	rax, 63					; 0000003fH
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 68   :     const size_t mask = coords.size() - 1;

	add	rdx, rax
	mov	QWORD PTR mask$1$[rsp], rdx

; 69   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	test	r8d, r8d
	jle	$LN134@read
	mov	QWORD PTR [r11+32], rbx
	movzx	ebx, BYTE PTR $T6[rsp]
	mov	QWORD PTR [r11-48], rdi
	movzx	edi, BYTE PTR $T5[rsp]
	mov	QWORD PTR [r11-40], rsi
	mov	QWORD PTR [r11-56], r14
$LL4@read:
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 566  :     return typelist_internal::TSEvalFirstIndex<OpT, RetT, Start, End>(op, def);

	mov	QWORD PTR $T3[rsp], rcx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 69   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	mov	eax, ebp
	and	rax, rdx
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	movzx	edx, dil
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
	mov	rax, QWORD PTR [r13]
	lea	rcx, QWORD PTR [rax+rcx*4]
	mov	QWORD PTR $T3[rsp+8], rcx
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	lea	rcx, QWORD PTR $T3[rsp]
	call	??$?RU?$integral_constant@_K$0A@@std@@@<lambda_5bb52e0367d4c99b6fde692a40776041>@@QEBAPEBGU?$integral_constant@_K$0A@@std@@@Z ; <lambda_5bb52e0367d4c99b6fde692a40776041>::operator()<std::integral_constant<unsigned __int64,0> >
	test	rax, rax
	jne	$LN73@read

; 517  :         return TSEvalFirstIndex<OpT, RetT, Iter+1, End>(op, def);

	mov	r14, QWORD PTR $T3[rsp+8]
	mov	rsi, QWORD PTR $T3[rsp]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, DWORD PTR [r14]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rsi+548]
	jne	SHORT $LN133@read
	mov	eax, DWORD PTR [r14+4]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rsi+552]
	jne	SHORT $LN133@read
	mov	eax, DWORD PTR [r14+8]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rsi+556]
	jne	SHORT $LN133@read

; 473  :                     return &(node->getValueAndCache(xyz, *this));

	mov	rcx, QWORD PTR [rsi+592]
	mov	r8, rsi
	mov	rdx, r14
	call	??$getValueAndCache@$$CBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@QEBAAEBGAEBVCoord@math@23@AEBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>::getValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > const >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	test	rax, rax
	jne	SHORT $LN73@read
$LN133@read:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, DWORD PTR [r14]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rsi+560]
	jne	SHORT $LN74@read
	mov	eax, DWORD PTR [r14+4]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rsi+564]
	jne	SHORT $LN74@read
	mov	eax, DWORD PTR [r14+8]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rsi+568]
	jne	SHORT $LN74@read

; 473  :                     return &(node->getValueAndCache(xyz, *this));

	mov	rcx, QWORD PTR [rsi+584]
	mov	r8, rsi
	mov	rdx, r14
	call	??$getValueAndCache@$$CBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEBAAEBGAEBVCoord@math@23@AEBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::getValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > const >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	test	rax, rax
	jne	SHORT $LN73@read
$LN74@read:
	movzx	edx, bl

; 517  :         return TSEvalFirstIndex<OpT, RetT, Iter+1, End>(op, def);

	mov	QWORD PTR $T4[rsp], rsi

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	lea	rcx, QWORD PTR $T4[rsp]

; 517  :         return TSEvalFirstIndex<OpT, RetT, Iter+1, End>(op, def);

	mov	QWORD PTR $T4[rsp+8], r14

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	call	??$?RU?$integral_constant@_K$02@std@@@<lambda_5bb52e0367d4c99b6fde692a40776041>@@QEBAPEBGU?$integral_constant@_K$02@std@@@Z ; <lambda_5bb52e0367d4c99b6fde692a40776041>::operator()<std::integral_constant<unsigned __int64,3> >
$LN73@read:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 69   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	movzx	ecx, WORD PTR [rax]
	inc	ebp
	mov	rdx, QWORD PTR mask$1$[rsp]
	add	r15, rcx
	mov	rcx, QWORD PTR a$[rsp]
	cmp	ebp, r12d
	jl	$LL4@read
	mov	r14, QWORD PTR [rsp+64]
	mov	rdi, QWORD PTR [rsp+72]
	mov	rsi, QWORD PTR [rsp+80]
	mov	rbx, QWORD PTR [rsp+152]
$LN134@read:

; 70   :     return sum;
; 71   : }

	mov	rax, r15
	add	rsp, 88					; 00000058H
	pop	r15
	pop	r13
	pop	r12
	pop	rbp
	ret	0
?read@@YA_KAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@H@Z ENDP ; read
