"""Phase 5 checkpoint A: boundary/ending hints + forward-only Shorten A.

One meaningful family, never three copies. Extend still delegates explicitly to
Phase 4. Highlight, Intro detection, families B/C and selectable Loop are later.
"""
from __future__ import annotations
from dataclasses import dataclass, field

from .core import (Analysis, Budget, Config, FitError, Plan, _graph_plans,
                   _SearchProfile, target_frames, validate_plan)
from .structure import StructureHints, build_structure_hints

PLANNER_VERSION = 'phase5-a/1'
MAX_SHORTEN_EDITS = 4


@dataclass
class Arrangement:
    candidates: list[Plan]
    hints: StructureHints | None
    diagnostics: dict = field(default_factory=dict)


def validate_shortening(a: Analysis, p: Plan, c: Config,
                        endings: tuple[int, ...], max_edits: int) -> None:
    """Check the actual edit map; a successful search is not itself proof."""
    validate_plan(a, p, c)
    minimum = min(round(c.min_run_seconds * a.sample_rate), p.target_frames // 4)
    if p.spans[0].start != 0 or p.spans[0].end < round(c.keep_intro_seconds*a.sample_rate):
        raise FitError('phase5_intro_not_preserved')
    if len(p.spans)-1 > max_edits or any(s.end-s.start < minimum for s in p.spans):
        raise FitError('phase5_edit_or_dwell_limit')
    if any(r.start <= l.end for l,r in zip(p.spans,p.spans[1:])):
        raise FitError('phase5_nonforward_jump')
    if any(score < c.min_similarity for score in p.transition_scores):
        raise FitError('phase5_weak_seam')
    if p.ending == 'source_end':
        if p.spans[-1].end != a.frames or not any(
                p.spans[-1].start <= e < a.frames for e in endings):
            raise FitError('phase5_ending_not_preserved')


def arrange(a: Analysis, seconds: float, config: Config | None = None,
            budget: Budget | None = None) -> Arrangement:
    c, b = config or Config(), budget or Budget()
    c.validate(); b.check()
    target = target_frames(seconds, a.sample_rate, c)
    intro = round(c.keep_intro_seconds*a.sample_rate)
    outro = round(c.keep_outro_seconds*a.sample_rate)
    if intro + outro > min(target,a.frames):
        # Explicit user protection is NEVER silently relaxed.
        raise FitError('protected_prefix_suffix_exceed_duration')
    diagnostics = {'version': PLANNER_VERSION, 'checkpoint': 'shorten_family_a',
                   'implemented_families': ['structure_preserve'],
                   'human_naturalness_proven': False, 'search_attempts': []}
    if target >= a.frames:
        # Do not pretend the dedicated Extend policy has been implemented.
        result = _graph_plans(a,seconds,c,b,phase=4)
        diagnostics.update(mode='unchanged' if target == a.frames else 'extend',
                           implementation='legacy_phase4_delegate', implemented_families=[],
                           fallback_reason='dedicated_extend_policy_not_yet_implemented')
        return Arrangement(result,None,diagnostics)

    hints = build_structure_hints(a,c,b)
    diagnostics.update(mode='shorten', implementation='shared_graph_forward_only',
                       max_edits=min(c.max_jumps,MAX_SHORTEN_EDITS))
    minimum = min(round(c.min_run_seconds*a.sample_rate), target//4)
    # At least the protected head + a playable head segment + selected tail must fit.
    head = max(intro,minimum)
    feasible = [e for e in hints.ending_entries
                if max(outro,minimum) <= a.frames-e.frame <= target-head]
    substantial = [e for e in feasible if e.kind == 'coarse_boundary'
                   or a.frames-e.frame >= round(8*a.sample_rate)]
    tiers = [('structural_or_long_tail',substantial),
             ('shorter_source_tail',feasible)]
    tested_entries: set[tuple[int, ...]] = set()
    result: list[Plan] = []
    selected_endings: tuple[int, ...] = ()
    used_tier = ''
    for tier, entries in tiers:
        b.check()
        signature = tuple(sorted(e.frame for e in entries))
        if not entries or signature in tested_entries:
            continue
        tested_entries.add(signature)
        profile = _SearchProfile(tuple(e.frame for e in entries),
                                 tuple(e.strength for e in entries),
                                 max_edits=min(c.max_jumps,MAX_SHORTEN_EDITS))
        result = _graph_plans(a,seconds,c,b,phase=4,_profile=profile)
        diagnostics['search_attempts'].append({'tier': tier, 'ending_count': len(entries),
                                                'found': bool(result)})
        if result:
            selected_endings = profile.ending_entries
            used_tier = tier
            break
    if not result:
        b.check()  # Cancellation/exhaustion is an error, NEVER a fallback success.
        profile = _SearchProfile((),(),terminal_mode='fade',
                                 max_edits=min(c.max_jumps,MAX_SHORTEN_EDITS))
        result = _graph_plans(a,seconds,c,b,phase=4,_profile=profile)
        used_tier = 'fade_fallback'
        diagnostics['search_attempts'].append({'tier': used_tier, 'ending_count': 0,
                                                'found': bool(result)})
    diagnostics['tier'] = used_tier
    diagnostics['fallback_reason'] = (None if used_tier == 'structural_or_long_tail' else
        'no_preferred_ending_route_in_bounded_search' if used_tier == 'shorter_source_tail' else
        'no_source_end_route_in_bounded_search')
    diagnostics['selected_ending'] = None
    for p in result:
        validate_shortening(a,p,c,selected_endings,min(c.max_jumps,MAX_SHORTEN_EDITS))
        p.strategy = 'structure_preserve' if p.ending == 'source_end' else 'shorten_fade_fallback'
        p.warnings += ['phase5_checkpoint_a;only_one_family_implemented']
        if diagnostics['fallback_reason']:
            p.warnings.append(diagnostics['fallback_reason'])
        if p.ending == 'source_end':
            matches = [e for e in hints.ending_entries if e.frame in selected_endings
                       and p.spans[-1].start <= e.frame]
            chosen = min(matches,key=lambda e:(-e.strength,e.frame))
            diagnostics['selected_ending'] = {
                'entry_frame': chosen.frame, 'kind': chosen.kind,
                'strength': chosen.strength, 'source_tail_frames': a.frames-chosen.frame,
                'continuous_final_span_start': p.spans[-1].start,
                'source_end_frame': a.frames}
    b.check()
    return Arrangement(result,hints,diagnostics)
